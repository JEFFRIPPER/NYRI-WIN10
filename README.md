# NYRI-WIN10

Нативный Windows-порт desktop-shell [Nyri](https://github.com/yzewe/Nyri) в стиле Material 3 Expressive для Windows 10 и Windows 11. Цель — единая среда рабочего стола: панель, док, поиск, шторка, виджеты, обои и настройки. Live Island — один из модулей этой среды.

Проект не заменяет Explorer и не пытается запускать Linux-оболочку через WSL. Это отдельное WPF-приложение поверх рабочего стола Windows.

## Статус

**v0.3 — фундамент порта: реализован Live Island и часть нативных Windows-провайдеров. Полноценная desktop-shell пока в разработке.**

Подробный разбор оригинала, Material 3 Expressive и карта переноса: [docs/NYRI-UPSTREAM-STUDY.md](docs/NYRI-UPSTREAM-STUDY.md). Референс зафиксирован на upstream-коммите `660cf73`, поддерживающем Niri и Hyprland.

Уже работает:

- прозрачное always-on-top окно без рамки;
- компактное и раскрытое состояние острова;
- плавная анимация размера и раскрытия;
- часы;
- event-driven отслеживание доступности сети;
- event-driven отслеживание Windows Clipboard;
- Windows Global Media Session: текущий трек из Spotify/браузеров/плееров;
- нативные media controls: previous / play-pause / next;
- event-driven отслеживание VPN-интерфейсов;
- приоритеты активностей по модели оригинального Nyri;
- защита от запуска нескольких экземпляров;
- сохранение позиции острова после Shift + drag;
- прокручиваемый список активностей;
- история активностей в раскрытом режиме;
- перенос острова мышью через `Shift + drag`;
- `Ctrl + R` возвращает остров в центр сверху;
- `Esc` сворачивает раскрытый остров;
- `Ctrl + Q` закрывает NYRI.

## Совместимость

- Windows 10 2004+ x64;
- Windows 11 x64;
- .NET 8 для разработки;
- релиз можно публиковать self-contained, поэтому конечному пользователю .NET устанавливать не требуется.
## Сборка

```powershell
dotnet build NYRI-WIN10.sln -c Release
```

Self-contained EXE:

```powershell
dotnet publish src\Nyri.Win10\Nyri.Win10.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\win-x64
```

## Архитектура

```text
src/Nyri.Win10/
├─ Models/       # типы активностей острова
├─ Services/     # Windows event providers
├─ MainWindow.*  # UI, анимации, позиционирование
└─ App.xaml      # базовые Material-подобные токены
```

Центральный `ActivityHub` принимает события от провайдеров и определяет главную активность, которую показывает компактный остров. Раскрытый режим показывает несколько последних активностей.

Сейчас реализованы провайдеры сети, Clipboard, Global Media Session и VPN. Также реализованы таймер/секундомер и best-effort индикаторы микрофона/камеры. Следующие провайдеры: передачи файлов и устройства.

## Roadmap

Приоритет после системного фундамента — полноценная оболочка по архитектуре оригинала:

- [ ] общий ShellRuntime и маршрутизация панелей
- [ ] Material 3 Expressive: semantic colors, type, shape, motion
- [ ] настраиваемая верхняя/нижняя панель с Live Island
- [ ] launcher с реальными приложениями и окнами
- [ ] dock закреплённых и запущенных приложений
- [ ] control center с рабочими действиями и страницами
- [ ] desktop widgets, раскладка и обои
- [ ] отдельные настройки оболочки
- [ ] notification center, OSD и история буфера


- [x] WPF overlay / always-on-top island
- [x] compact / expanded states
- [x] animated resizing
- [x] Clipboard events
- [x] network events
- [x] Windows Global Media Session
- [x] microphone / camera privacy activity (best effort)
- [x] VPN state
- [x] single-instance guard
- [x] persistent island position
- [x] timers / stopwatch
- [ ] file-copy progress
- [ ] Bluetooth / headset battery
- [ ] autostart + tray settings
- [ ] Material You palette from wallpaper
- [ ] multi-monitor placement
- [ ] installer / updater

## Происхождение идеи

NYRI-WIN10 вдохновлён проектом [yzewe/Nyri](https://github.com/yzewe/Nyri), который является Linux desktop-shell для Niri/Hyprland + Quickshell. Исходный Nyri распространяется под MIT License.

Этот репозиторий — отдельная Windows-реализация: Linux/Wayland-код Nyri здесь не запускается и напрямую не переносится.

## Важно

v0.3 — фундамент проекта с рабочими Windows events, media controls и сохранением состояния. API провайдеров и визуальные параметры ещё будут меняться.

## Privacy-индикаторы

Микрофон и камера показаны независимо в компактном острове; использование захвата имеет приоритет 100 в ActivityHub. PrivacyService читает только метаданные HKCU Windows CapabilityAccessManager ConsentStore и наблюдает изменения дерева через RegNotifyChangeKeyValue. Дополнительная сверка раз в 10 секунд восстанавливает состояние при пропущенных событиях; если дерево отсутствует, повторное обнаружение выполняется раз в 2 секунды. Устройства захвата не открываются, звук и видео не записываются, разрешения Windows не изменяются.

Это best effort: ConsentStore не является гарантированным публичным API для определения всех сессий. Приложения, которые не отражены Windows в этом дереве, не будут показаны; отсутствие индикатора не означает отсутствие записи. Недоступные метаданные отображаются отдельным сообщением. Имена приложений определяются из ключей Windows, для упакованных приложений могут отображаться идентификаторы пакетов.

ActivityHub доставляет изменения в UI dispatcher, подавляет повторные неизменённые состояния и сохраняет все активные провайдеры. Кнопки поддерживают видимый фокус клавиатуры; анимации учитывают системную настройку Windows.

Проверка событий и privacy-парсера (изолированные временные ключи реестра, без запуска камеры/микрофона):

```powershell
dotnet run --project tests/Nyri.SmokeTests -c Release
```

## Запуск и восстановление окна

Запускай `dist\win-x64\NYRI-WIN10.exe` (или EXE выбранной preview-сборки). Остров появляется вверху экрана и не имеет кнопки в панели задач. Повторный запуск EXE возвращает и активирует уже работающий остров вместо молчаливого выхода второго экземпляра; сохранённая позиция остаётся прежней, если она доступна на рабочем столе. Закрытие: `Ctrl + Q`, когда остров в фокусе.

Позиция, часы и заголовок настраиваются до ожидания Media Session API. Краткий журнал запуска и активации: `%LOCALAPPDATA%\NYRI-WIN10\startup.log`; содержимое Clipboard и сведения о треках туда не записываются.
