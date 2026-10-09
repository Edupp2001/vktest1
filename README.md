# Wukong Benchmark Automator

Консольная утилита для автоматизированного запуска Black Myth: Wukong Benchmark Tool.
Выполняет два прогона (CPU-профиль и GPU-профиль), собирает результаты и валидирует их
через два независимых источника данных.

## Возможности

- Автоматическая настройка `GameUserSettings.ini` под CPU и GPU профили
- Запуск через Steam (`-applaunch`) — без диалога подтверждения
- Чтение результатов из JSON, которые пишет игра после каждого прогона
- Независимая верификация через `ReadProcessMemory` (live-сэмплинг FPS)
- Graceful shutdown: Ctrl+C, закрытие окна, logoff, shutdown
- Итоговый JSON-отчёт для CI/CD
- Проверки на входе: конфиг игры, исполняемый файл, дубликаты процесса

## Требования

- Windows 10/11 x64
- Steam с установленным **Black Myth: Wukong Benchmark Tool** (App ID `3132990`)
- .NET 10 Runtime (или self-contained сборка)

## Использование
- WukongBenchmarkAutomator.exe # обычный режим
- WukongBenchmarkAutomator.exe -log # с подробным логом


### Флаги

| Флаг | Описание |
|---|---|
| `-log`, `--log`, `-v` | Включить подробный вывод (все DEBUG-сообщения) |

### Exit codes

| Код | Значение |
|---|---|
| `0` | Успех |
| `1` | Файлы игры не найдены / игра уже запущена |
| `2` | Бенчмарк не завершился (игра упала или не создала файл результатов) |
| `3` | Прочая ошибка |

## Выходные файлы

- **Консоль** — читаемый отчёт с таблицами CPU/GPU и валидацией
- **JSON-отчёт** — `benchmark_YYYYMMDD_HHMMSS.json` в папке приложения

## Архитектура

| Компонент | Ответственность |
|---|---|
| `GameConfigManager` | Чтение/запись `GameUserSettings.ini`, бэкап и восстановление |
| `BenchmarkHistoryReader` | Отслеживание появления JSON-файлов в `%TEMP%\b1\BenchMarkHistory\Tool` |
| `MemoryFpsMonitor` | Сэмплинг live-FPS через `ReadProcessMemory` каждые 50 мс |
| `SteamLauncher` | Запуск через `steam.exe -applaunch <appid>` |
| `SteamPopupHandler` | Fallback: автозакрытие диалога Steam, если `-applaunch` не сработал |
| `GracefulShutdown` | Перехват Ctrl+C, X, logoff, shutdown, unhandled exception |
| `Log` | Единый логгер с уровнями Info/Debug/Warn/Error и InvariantCulture |
| `BenchmarkReport` | Сборка итогового JSON-отчёта |

## Как это работает

1. Проверяются пути к файлам игры и отсутствие запущенной копии.
2. Делается бэкап оригинального `GameUserSettings.ini`.
3. Применяются настройки CPU-профиля (минимальное разрешение, RTX off).
4. Игра запускается через `steam.exe -applaunch`.
5. Параллельно работает `MemoryFpsMonitor`, читающий FPS из памяти.
6. Ожидание появления нового файла результатов в `%TEMP%`.
7. Повтор для GPU-профиля (4K, RTX Ultra).
8. Валидация: сверка JSON-результатов и данных из памяти.
9. Восстановление оригинального конфига.
10. Сохранение JSON-отчёта.

## Сборка в один .exe

```bash
dotnet publish -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true
или используйте WukongBenchmarkAutomator.exe из release
