# cursor-1c-diff

Тихий лаунчер **Cursor** для внешней программы сравнения/объединения в **Конфигураторе 1С**.

- без окна `cmd`
- выгрузки 1С (`.txt`) копируются во временные `.bsl`, чтобы работали язык BSL и language server
- при сравнении двух файлов порядок left/right берётся из имён выгрузки 1С (1С часто путает стороны). Справа более старшая сторона:
  - внешний файл `(Файл …)` — всегда справа
  - если есть номер версии хранилища — старший номер справа
  - `Основная конфигурация` считается самой старшей версией (правее любой `Конфигурация хранилища N`)

## Сборка

Нужен PowerShell 7 и `csc.exe` (.NET Framework 4.x):

```powershell
pwsh -NoProfile -File .\build.ps1
```

Результат: `dist\Cursor1cDiff.exe`.

## Настройка в Конфигураторе

Сервис → Параметры → Сравнение/объединение → внешняя программа:

| Поле | Значение |
|------|----------|
| Исполняемый файл | путь к `Cursor1cDiff.exe` |
| Сравнение двух файлов | `%secondCfg %baseCfg` |
| Объединение двух файлов | `%baseCfg %secondCfg %merged` |
| Трехстороннее объединение | `%oldVendorCfg %baseCfg %secondCfg %merged` |
| Автоматическое трехстороннее объединение | `%oldVendorCfg %baseCfg %secondCfg %merged` |

В списке «Показывать различия без учета структуры» выберите эту программу.

## Репозиторий

- GitHub: [makeinstall77/cursor-1c-diff](https://github.com/makeinstall77/cursor-1c-diff) (remote `github`)
- Origin (Cursor): [makeinstall/cursor-1c-diff](https://cursor.com/codebase/makeinstall/cursor-1c-diff) (remote `origin`)
