# Настройка TCP Timestamps в Windows

**TCP Timestamps** — критическая функция сетевого стека, обеспечивающая точное измерение времени задержки (RTT), защиту от обертывания sequence numbers и совместимость с современными сетевыми стандартами. Отключение этой опции может привести к проблемам с подключением к некоторым сервисам, особенно при работе за прокси или в условиях высокой сетевой задержки. 

> **Важно!** Все операции требуют прав **локального администратора**. Изменения вступают в силу немедленно без перезагрузки, но могут повлиять на сетевую производительность в специфических сценариях.

---

## Стандартный метод: через netsh
1. **Откройте командную строку или PowerShell от имени администратора**  
   Нажмите `Win + X` и выберите «Терминал Windows (администратор)» или «Командная строка (администратор)».

2. **Включите TCP Timestamps**  
   Выполните команду:
   ```cmd
   netsh interface tcp set global timestamps=enabled
   ```
   > `enabled` — активирует использование TCP timestamps. 

3. **Проверьте статус**  
   ```cmd
   netsh interface tcp show global
   ```
   В выводе найдите параметр `Timestamps` — значение должно быть `enabled`. 

---

## Альтернативный метод: через PowerShell
> **Примечание**  
> Этот метод предпочтителен для Windows 10/11 и Server 2016+, так как использует нативные PowerShell командлеты.

1. Запустите **PowerShell от имени администратора**.

2. Выполните команду:
   ```powershell
   Set-NetTCPSetting -SettingName InternetCustom -Timestamps enabled
   ```
   > Используйте `InternetCustom` для настройки под внешние подключения или `DatacenterCustom` для внутренних сетей. 

3. Подтвердите изменения:
   ```powershell
   Get-NetTCPSetting | Where-Object { $_.SettingName -like "*Custom*" } | Select-Object SettingName, Timestamps
   ```

---

## Диагностика проблем
Если TCP Timestamps не включаются или отключаются автоматически:

### 1. Проверка групповых политик
- Выполните `gpedit.msc` и проверьте путь:  
  `Конфигурация компьютера → Административные шаблоны → Сеть → Параметры TCP/IP`  
- Убедитесь, что политика **«Включение TCP-меток времени»** не переопределена.

### 2. Анализ сетевых драйверов
- Обновите драйверы сетевого адаптера до последней версии с сайта производителя.
- Отключите специфичные функции драйвера (TCP Chimney Offload, Large Send Offload), которые могут конфликтовать с TCP Timestamps:
  ```powershell
  Get-NetAdapter | Disable-NetAdapterLso
  ```

### 3. Проверка файрвола и антивируса
- Временно отключите сторонние сетевые фильтры:
  ```powershell
  Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False
  ```
  > Не забудьте вернуть настройки после диагностики! 

---

## Оптимизация и безопасность
Хотя TCP Timestamps улучшают производительность, они могут использоваться для определения uptime системы. Для баланса между безопасностью и производительностью:

```powershell
Set-NetTCPSetting -SettingName InternetCustom `
    -Timestamps enabled `
    -CongestionProvider CTCP `
    -InitialRtoMs 2000 `
    -EcnCapability Disabled
```
> **Важно:** Для публичных серверов рекомендуется отключить ECN при использовании timestamps. 

---

## Восстановление настроек по умолчанию
Если изменения вызвали проблемы с сетью:
```cmd
netsh int tcp set global default
```
Или для конкретного параметра:
```powershell
Set-NetTCPSetting -SettingName InternetCustom -Timestamps default
```