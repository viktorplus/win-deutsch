# Архитектура Win Deutsch

Документ описывает реальный код. Пути — от корня репозитория.

## Выбор стека

| Критерий | C# + WPF (.NET 8) | C# + WinUI 3 | Tauri | Electron | Python + PySide6 |
|---|---|---|---|---|---|
| Always-on-top, прозрачность, окно без фокуса | нативно (`Topmost`, `AllowsTransparency`, `WS_EX_NOACTIVATE`) | прозрачность окна ограничена, нужен Win32 interop | через WebView2, прозрачность с оговорками | есть | есть |
| Трей | `NotifyIcon` из WinForms, в коробке | нет, сторонние пакеты | есть | есть | есть |
| Автозапуск | ключ реестра HKCU\Run | то же, но для MSIX — свой механизм | плагин | пакет | реестр |
| Память в простое (оценка) | ~50–80 МБ | ~60–100 МБ | ~30–60 МБ + WebView2 | 150–250 МБ | 60–120 МБ |
| Установка | один self-contained exe | MSIX/упаковка, сложнее | маленький exe, но WebView2 и сборка Rust | большой дистрибутив | упаковка PyInstaller, медленный старт |
| Локальная БД | Microsoft.Data.Sqlite | то же | rusqlite/плагин | better-sqlite3 (нативная сборка) | sqlite3 в stdlib |
| Инструменты на машине | .NET 8 SDK | SDK + Windows App SDK | нужен Rust + MSVC | есть Node/pnpm | есть Python 3.14, PySide6 под 3.14 не проверен |
| Развитие | зрелый, много примеров | молодой, меняющийся API | две кодовые базы (Rust + JS) | тяжёлый для фоновой утилиты | слабее для нативного Windows-UX |

**Выбор: C# + WPF на .NET 8.** Суть программы — окно поверх всех без фокуса, прозрачность, трей,
реестр, глобальные горячие клавиши, детект полноэкранного режима — делается штатными средствами
и парой вызовов Win32. Цифры памяти в таблице — предварительные оценки; фактический замер
опубликованного exe — в `README.md`, раздел «Ресурсы». Ориентир «до 100 МБ» оценивается по
приватному рабочему набору (то, что Диспетчер задач показывает по умолчанию): 45,9 МБ через 25 с
после старта — укладывается. Полный рабочий набор (135 МБ, в основном общие отображённые
страницы single-file бандла) указан там отдельно и для ориентира не используется.

Зависимости: `Microsoft.Data.Sqlite`; для тестов `xunit`, `xunit.runner.visualstudio`,
`Microsoft.NET.Test.Sdk`. Трей — `System.Windows.Forms.NotifyIcon` (`UseWindowsForms=true`),
JSON — `System.Text.Json`, время — `TimeProvider`.

## Структура проекта

```
WinDeutsch.sln
Directory.Build.props
src/WinDeutsch.Core/        net8.0, без ссылок на WPF/WinForms
  AppPaths.cs, Log.cs
  Models/Models.cs          Topic, Phrase, ShowEvent, перечисления
  Storage/                  Database, PhraseRepository, Statistics
  Learning/                 LearningRules, PhraseSelector, QuizBuilder
  Scheduling/               ShowScheduler, PauseOption
  Import/                   ImportParser, ImportService, TextFileReader, Exporter, DictionaryFolder
  Settings/                 AppSettings, SettingsStore, IntervalKind
  Backup/                   BackupService
  Seed/                     SeedData + 8 файлов *.tsv (EmbeddedResource)
src/WinDeutsch.App/         net8.0-windows, WPF, AssemblyName=WinDeutsch
  App.xaml(.cs)             старт, глобальный обработчик исключений
  AppController.cs          единственная точка состояния и таймера
  Cards/                    CardView (UserControl), CardViewModel
  Windows/                  WidgetWindow, PopupWindow, LibraryWindow, SettingsWindow,
                            ImportDialog, PhraseEditDialog, Library/*, Settings/*
  Tray/TrayIcon.cs
  Platform/                 NativeMethods, FullscreenDetector, IdleDetector, HotkeyManager,
                            AutostartRegistry, ScreenHelper, NoActivateWindow, SingleInstance
dictionaries/               <Категория>/<Тема>.tsv; копируются в dictionaries\ рядом с exe
tests/WinDeutsch.Core.Tests/  xunit, только логика Core
scripts/publish.ps1         сборка dist\WinDeutsch.exe
```

Вся логика — в Core и проверяется тестами без UI. App — тонкий слой окон и Win32.

## Модель данных

SQLite, `PRAGMA user_version = 1`, `PRAGMA foreign_keys = ON`. Всё время — UTC, ISO-8601
строкой. Схема создаётся в `Database` (`src/WinDeutsch.Core/Storage/Database.cs`):

```sql
topics(id PK, name NOT NULL, category NULL, is_enabled DEFAULT 1, is_built_in DEFAULT 0, sort_order DEFAULT 0)
phrases(id PK, topic_id NOT NULL -> topics ON DELETE CASCADE, german NOT NULL, russian NOT NULL DEFAULT '',
        status DEFAULT 0, level DEFAULT 0, repeat_more DEFAULT 0, snoozed_until NULL,
        show_count, correct_count, wrong_count DEFAULT 0, last_shown_at NULL, created_at NOT NULL)
show_events(id PK, phrase_id NOT NULL -> phrases ON DELETE CASCADE, at NOT NULL, mode NOT NULL, outcome NOT NULL)
индексы: ix_phrases_topic, ix_events_at, ix_events_phrase
```

Записи модели — `Topic`, `Phrase`, `ShowEvent` (`src/WinDeutsch.Core/Models/Models.cs`).
Перечисления: `PhraseStatus` (Active/Excluded), `ShowOutcome` (Shown, Correct, Wrong, Known,
Unknown, Later, Excluded, RepeatMore), `DisplayMode` (Widget/Popup), `TranslationMode`
(Immediate/OnClick/Delayed/Never), `Familiarity` (New/Unknown/Learning/Known — вычисляется,
в базе не хранится).

## Хранение

- `Database` владеет файлом и схемой, соединения открываются с `Pooling=false` — файл
  освобождается сразу после `Dispose` (нужно для бэкапа, восстановления и переименования).
  Схема создаётся и проверяется по `user_version`; без ORM (схема из трёх таблиц).
- `PhraseRepository` — весь доступ к данным: темы, фразы, групповые операции, массовая вставка
  одной транзакцией (`AddPhrasesBulk`), кандидаты на показ (`GetCandidates`: активна, тема
  включена, не отложена, немецкий не пуст), события, история, статистика.
- Папка данных: `AppPaths.DataDir` = `%WINDEUTSCH_DATA_DIR%` или `%LOCALAPPDATA%\WinDeutsch`,
  создаётся при первом обращении. Файлы: `data.db`, `settings.json`, `log.txt`, `backups\`.
- Базовый набор: 8 файлов `src/WinDeutsch.Core/Seed/*.tsv` (встроенные ресурсы), `SeedData.EnsureSeeded`
  заполняет базу, когда нет ни одной темы; `SeedData.RestoreMissing` возвращает недостающие
  фразы по нормализованному немецкому тексту.
- Резервные копии: `BackupService.CreateBackup` (zip с `data.db`, снятым через SQLite, и
  `settings.json`). `BackupService.Restore` сначала кладёт автокопию в `backups\`, zip без
  `data.db` отвергается до любых изменений. Из приложения восстановление идёт через
  `AppController.RestoreFromBackup`, которая закрывает соединения, пересоздаёт
  `Database`/`PhraseRepository` и перечитывает настройки.
- Импорт/экспорт: `ImportParser` (разделители, `#`-комментарии), `TextFileReader`
  (UTF-8 или Windows-1251), `ImportService` (предпросмотр, дубликаты, темы из третьей колонки),
  `Exporter.ToTsv`.
- Словари: `DictionaryFolder` перечисляет `dictionaries\<категория>\<тема>.tsv` рядом с exe
  (`AppContext.BaseDirectory`) и добавляет файл в тему его категории через `ImportService`
  (дубликаты пропускаются, повторное добавление безопасно). При старте `AppController` подключает
  файлы, которых нет в `AppSettings.ImportedDictionaries` (`DictionaryFolder.ImportNew`), и
  дописывает их туда — удалённый словарь не возвращается. В single-file они не упаковываются,
  чтобы пользователь мог класть свои файлы.

## Настройки

`AppSettings` (`src/WinDeutsch.Core/Settings/AppSettings.cs`) — один `settings.json`, camelCase,
перечисления строками. Значения по умолчанию = значения первого запуска: режим Виджет, интервал
60 с, перевод виджета «через 4 с», карточки «сразу», тесты 20 %, тёмная тема, карточка в правом
нижнем углу на мониторе с курсором, скрытие при полноэкранных включено, простой 5 мин.
`SettingsStore.Save` читает существующий файл как `JsonObject`, перезаписывает известные ключи
и сохраняет неизвестные; значения зажимаются в допустимые диапазоны. Повреждённый файл
переименовывается в `settings.json.broken-<время>`, загружаются значения по умолчанию
(`LastLoadWasBroken`, `BrokenFilePath`). `SettingsWindow` применяет изменения сразу через
`AppController.ApplySettings`; неверное значение подсвечивается и не сохраняется.

## Статистика

`PhraseRepository.GetStatistics()` возвращает `Statistics`: всего фраз; New/Unknown/Learning/Known
(по активным) и Excluded; показы сегодня и за 7 дней; тесты сегодня и всего с числом верных;
10 самых трудных фраз (по `WrongCount > 0`). Показ — событие `Shown`, тест — `Correct`/`Wrong`.
«Сегодня» — по локальной дате `TimeProvider.LocalTimeZone`; «7 дней» — сегодня плюс 6 предыдущих
локальных дней. Источник цифр — таблицы `phrases` и `show_events`. История
(`GetHistory`) — последние 500 событий во вкладке «История» (`LibraryWindow`).

## Алгоритм выбора следующей фразы

`PhraseSelector.Pick(candidates, recentIds)` — чистая функция, `Random` передаётся снаружи.

1. Вес по уровню (`LearningRules.Weight`): уровни 0..5 → 8, 6, 4, 2, 1, 0,5; «новая»
   (`ShowCount == 0 && Level == 0`) → 8; `RepeatMore` умножает вес на 2.
   **D01:** «Новая» определяется как `ShowCount == 0 && Level == 0`; ручной уровень > 0 у ни разу
   не показанной фразы сразу задаёт её состояние и вес по уровню.
2. Анти-повтор: исключаются последние `min(10, кандидатов / 2)` показанных id (самые свежие
   первыми); если кандидат один — выбирается он.
3. Взвешенный случайный выбор из оставшихся.

Правила исходов (`LearningRules.Apply`):

| Исход | Эффект |
|---|---|
| Shown | `ShowCount+1`, `LastShownAt = now` |
| Correct | уровень +1 (макс. 5), `CorrectCount+1`, `RepeatMore = false` |
| Wrong | уровень −2 (мин. 0), `WrongCount+1` |
| Known | уровень 5, `RepeatMore = false` |
| Unknown | уровень 0 |
| Later | `SnoozedUntil = now + 1 ч` |
| Excluded | `Status = Excluded` |
| RepeatMore | уровень 0, `RepeatMore = true` |

Состояние для показа (`LearningRules.GetFamiliarity`): New — `ShowCount == 0 && Level == 0`;
уровни 0–1 — Unknown; 2–4 — Learning; 5 — Known.

## Режим тестов

`QuizBuilder.Build(target, pool)` строит вопрос «Что это означает?» с четырьмя вариантами
перевода. Неверные варианты — переводы других фраз: сначала из той же темы и близкие по длине
(±50 % символов), без совпадающих с правильным текстом; не хватает — из всех тем. Если у фразы нет
перевода или различных вариантов меньше трёх — возвращается `null`, и показ идёт обычной
карточкой. Правильный вариант стоит на случайной позиции (`Quiz.CorrectIndex`). Пул — активные
фразы с непустым русским. Доля тестов — `AppSettings.QuizPercent` (0–100); при выключенных тестах
(`QuizEnabled`) показ пассивный. Ответ идёт через `AppController.AnswerQuiz(index)`
(-1 — «Не знаю»), подсветка держится 2,5 с, в режиме карточки тест висит до ответа, но не дольше
`max(N, 60)` с; без ответа результат не записывается.

## UI

- `AppController` — единственная точка, где живут `PhraseRepository`, `SettingsStore`/`AppSettings`,
  `ShowScheduler`, `PhraseSelector`, `QuizBuilder`, таймер и история «назад» (до 50 фраз за сеанс).
  Окна зовут его методы (`Next`, `Previous`, `HandleOutcome`, `Pause`, `SetMode`, `OpenLibrary`,
  `ApplySettings`, `ReloadData`, `Exit`) и слушают `StateChanged`.
- `Cards/CardView.xaml` — одна разметка карточки (фраза, перевод, тест, кнопки), общая для
  `WidgetWindow` и `PopupWindow`; состояние — `CardViewModel`.
- `WidgetWindow` — перетаскиваемое окно с изменением размера за угол (отключается «Закрепить позицию»),
  `PopupWindow` — всплывающая карточка. Оба без фокуса и вне панели задач (`NoActivateWindow`).
- `LibraryWindow` — вкладки «Фразы», «Статистика», «История»; `ImportDialog`, `PhraseEditDialog`;
  «Словари…» (`TopicDialogs.PickDictionaries`), «Удалить категорию» (`PhraseRepository.DeleteTopics`);
  `SettingsWindow` — вкладки Показ · Тесты · Внешний вид · Поведение · Данные.
- `TrayIcon` — `NotifyIcon`, меню, серый значок на паузе, `ShowBalloon`.
- `Platform/`: `FullscreenDetector` (`SHQueryUserNotificationState` и размер окна переднего плана),
  `IdleDetector` (`GetLastInputInfo`, блокировка сеанса), `HotkeyManager` (`RegisterHotKey`, список
  `FailedHotkeys` для пометки «занято»), `AutostartRegistry` (`HKCU\...\Run`), `ScreenHelper`
  (мониторы, возврат окна на экран, смена конфигурации), `SingleInstance` (`Mutex` + событие
  активации).

## Фоновые таймеры

Один `DispatcherTimer` с тиком 1 с в `AppController`. На каждом тике: проверка срока паузы,
`ShowScheduler.IsDue()` и обратный отсчёт карточки. `ShowScheduler` — чистая логика «когда
следующий показ» (фиксированный или случайный интервал в границах; `IsInQuietHours`,
`PauseUntil`), время берётся из `TimeProvider`. Проверки окружения (полноэкранность, простой,
блокировка, тихие часы) идут не чаще раза в 2 с (`EnvironmentCheckInterval`) и только когда показ
должен состояться или виджет виден; в режиме «Карточка» окружение проверяется каждые 2 с всегда.
После возвращения пользователя отсчёт интервала начинается заново, показы не копятся.
Дополнительные короткие `DispatcherTimer` создаются только на разовые задержки (появление перевода,
подсветка теста).

## Надёжность

- Повреждённый `settings.json` → значения по умолчанию, файл сохраняется как
  `settings.json.broken-<время>`, одно уведомление в трее.
- `data.db` не открывается (`DatabaseCorruptException`) → файл переименовывается в
  `data.db.broken-<время>`, создаётся новая база с базовым набором, уведомление называет файл.
- Необработанные исключения (`App.xaml.cs`) и ошибки пишутся в `log.txt` через `Log.Error` (не бросает
  исключений сама); ошибка в одном окне или фразе не завершает программу.
- Один экземпляр: `SingleInstance`; второй запуск открывает библиотеку первого.
- Положение окон хранится в экранных координатах; вне всех мониторов окно возвращается в
  правый нижний угол основного (`ScreenHelper.EnsureOnScreen`).
- Нет сетевых обращений.
