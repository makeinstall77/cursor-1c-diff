# cursor-1c-diff

Тихий лаунчер **Cursor** для внешней программы сравнения/объединения в **Конфигураторе 1С**.

- без окна `cmd`
- выгрузки 1С (`.txt`) копируются во временные `.bsl`, чтобы работали язык BSL и language server

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

- Origin: [makeinstall/cursor-1c-diff](https://cursor.com/codebase/makeinstall/cursor-1c-diff)
- Clone: `https://origin.cursor.com/makeinstall/cursor-1c-diff.git`
