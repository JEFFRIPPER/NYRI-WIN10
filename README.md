# NYRI-WIN10

Нативный Windows-порт идеи [Nyri](https://github.com/yzewe/Nyri): компактный Live Island в стиле Material 3 Expressive для Windows 10 и Windows 11.

Проект не заменяет Explorer и не пытается запускать Linux-оболочку через WSL. Это отдельное WPF-приложение поверх рабочего стола Windows.

## Статус

**v0.2 — рабочий прототип с нативными Windows events.**

Уже работает:

- прозрачное always-on-top окно без рамки;
- компактное и раскрытое состояние острова;
- плавная анимация размера и раскрытия;
- часы;
- event-driven отслеживание доступности сети;
- event-driven отслеживание Windows Clipboard;
- Windows Global Media Session: текущий трек из Spotify/браузеров/плееров;
- event-driven отслеживание VPN-интерфейсов;
- приоритеты активностей по модели оригинального Nyri;
- история активностей в раскрытом режиме;
- перенос острова мышью через `Shift + drag`;
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

Сейчас реализованы провайдеры сети, Clipboard, Global Media Session и VPN. Следующие провайдеры проектируются так же: микрофон/камера, таймеры, передачи файлов и устройства.

## Roadmap

- [x] WPF overlay / always-on-top island
- [x] compact / expanded states
- [x] animated resizing
- [x] Clipboard events
- [x] network events
- [x] Windows Global Media Session
- [ ] microphone / camera privacy activity
- [x] VPN state
- [ ] timers / stopwatch
- [ ] file-copy progress
- [ ] Bluetooth / headset battery
- [ ] autostart + tray settings
- [ ] Material You palette from wallpaper
- [ ] multi-monitor placement
- [ ] installer / updater

## Происхождение идеи

NYRI-WIN10 вдохновлён проектом [yzewe/Nyri](https://github.com/yzewe/Nyri), который является Linux shell для niri + Quickshell. Исходный Nyri распространяется под MIT License.

Этот репозиторий — отдельная Windows-реализация: Linux/Wayland-код Nyri здесь не запускается и напрямую не переносится.

## Важно

v0.2 — фундамент проекта с рабочими Windows events. API провайдеров и визуальные параметры ещё будут меняться.
