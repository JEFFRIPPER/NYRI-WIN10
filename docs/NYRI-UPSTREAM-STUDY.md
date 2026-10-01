# Nyri: разбор оригинала и архитектура Windows-порта

Дата: 2 октября 2026, Europe/Moscow. Референс: [yzewe/Nyri](https://github.com/yzewe/Nyri), зафиксированный коммит [`660cf73f7f8c6d6c8e563fff79c2a8e78aa33127`](https://github.com/yzewe/Nyri/commit/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127).

Исследование основано на исходниках QML, служебных скриптах, конфигурации и штатных изображениях в docs/. Linux-сессия Quickshell здесь не запускалась: это анализ реализации, а не подтверждение работы всех функций на Linux или Windows. В дереве config/quickshell/nyri найдено 137 QML-файлов, в bin — 25 помощников. Составлена карта модулей; ключевые архитектурные, визуальные и системные реализации разобраны отдельно.

## 1. Цель проекта

Nyri — полноценная desktop-shell: оформление и поведение рабочего стола, панель, док, поиск, шторка, уведомления, виджеты, настройки, обои, переключение окон и дополнительные системные интерфейсы. Пользователь воспринимает эту среду как почти всю ОС. При этом оконный композитор, ядро и системные службы предоставляет Linux-окружение.

Актуальный оригинал поддерживает Niri и Hyprland. `services/Compositor.qml` выбирает адаптер по переменной окружения HYPRLAND_INSTANCE_SIGNATURE; оба адаптера дают shell единое представление окон, рабочих столов и фокуса. Возможности адаптеров не полностью одинаковы: например, activeCasts в Hyprland возвращает пустой список.

Windows-порт должен воспроизводить общую среду рабочего стола в Material 3 Expressive. Уже сделанный Live Island — сохраняемый модуль этой среды. Его отдельное окно не является завершённым портом Nyri.

Источники: [shell.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/shell.qml#L25), [Compositor.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/services/Compositor.qml#L6).

## 2. Оболочка собирается из отдельных поверхностей

`shell.qml` создаёт Wallpaper, Bar, Dock, OSD, notification Popups, Launcher, ControlCenter, Session, Clipboard, Picker, LockScreen, Switcher, Dashboard, специализированные popout, Settings, SnipMenu и PolkitDialog. Эти компоненты используют общие singleton-сервисы.

| Поверхность | Реальная организация в оригинале | Следствие для WPF |
|---|---|---|
| Обои | Background layer, отдельный экземпляр на экран, опциональное растягивание на несколько экранов | Отдельный WallpaperService и адаптер рабочего стола; фон не должен закрывать окна программ |
| Виджеты | Bottom layer, отдельный ввод по областям виджетов; повышенный слой в режиме редактирования | DesktopWidgetHost, размещение/размер/вариант/экран, управляемый Z-order и hit testing |
| Панель | Top или Overlay, резервирование зоны при постоянном показе, маска интерактивных участков | BarWindow на монитор, AppBar-регистрация для занятой полосы, автоскрытие и корректный DPI |
| Док | Bottom edge, Top/Overlay, резервирование только без автоскрытия | DockWindow, реальные закрепления и запущенные приложения, activation вместо дублирования процесса |
| Поиск и шторки | Прозрачная overlay-поверхность на выбранном экране, закрытие кликом снаружи, фокус по политике панели (ControlCenter: keyboard=false) | PanelRouter и отдельные OverlayWindow с согласованными Esc/focus/close правилами |
| Настройки | Обычное FloatingWindow | SettingsWindow со своей навигацией и сохранением параметров |

`Panels.qml` хранит текущую панель, страницу, активный экран и координаты элемента-источника. `Surface.qml` обеспечивает слой, ввод, scrim и закрытие; `Popout.qml` анимирует карточку от размеров кнопки-источника до раскрытого состояния. Прозрачное полноэкранное окно здесь является технической областью для ввода и размещения карточки, а не единым приложением, которое постоянно перекрывает рабочий стол.

Источники: [Panels.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/services/Panels.qml), [Surface.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/widgets/Surface.qml), [Wallpaper.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/wallpaper/Wallpaper.qml), [Bar.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/bar/Bar.qml), [Dock.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/dock/Dock.qml).

## 3. Основные пользовательские модули

| Модуль | Что переносить по смыслу |
|---|---|
| Панель | Три группы элементов; перенос элементов между группами; стили островки/полоса/чипы; верх/низ; часы, рабочие столы, название окна, tray, статус, поиск и шторка |
| Live Island / player | Приоритетные активности рядом с часами; раскрытие нужной страницы; обложка, транспорт, player selection, shuffle/repeat, позиция и lyrics |
| Док | Закреплённые и работающие приложения; увеличение при наведении; список окон; индикаторы запуска; уведомления; drag/reorder |
| Launcher | Поиск приложений, открытых окон, файлов, настроек и действий; калькулятор/конвертер; разбор команды таймера; поиск в интернете; режим команды |
| Control center | Громкость, яркость, Wi-Fi, Bluetooth, DND, профиль питания, удержание бодрствования, ночной свет, микрофон, privacy, тема; страницы Wi-Fi/BT/audio/privacy/phone, media/live/phone/timer/stopwatch-карусель, включая карточки запуска таймера и секундомера и уведомления |
| Desktop widgets | Часы, glance, батарея, media, прогноз, календарь, системные метрики и экранное время; варианты, сетка, перемещение, масштаб, каталог и привязка к рабочему столу |
| Wallpaper studio | Геометрические генераторы, палитры, светлая/тёмная версия, превью, масштаб и анимация; общий источник цвета среды |
| Settings | 12 разделов: оформление, стол/обои, окна, панель, док, уведомления, блокировка, питание/сон, время, поиск, клавиши, система; поиск по настройкам |
| Clipboard | История, поиск и действия, а не только короткая карточка последнего скопированного текста |
| Notifications / OSD | Всплывающие уведомления, история, действия и DND; отдельные индикаторы громкости/яркости |
| Switcher / session | Выбор реальных окон; профили приложений/раскладки и восстановление сеанса |
| Lock / snip / polkit | Оригинальные системные интеграции блокировки, захвата и повышения прав; Windows-реализация требует своих адаптеров |

Экранные примеры: [рабочий стол](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/docs/desktop.png), [шторка](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/docs/control.png), [поиск](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/docs/launcher.png), [настройки](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/docs/settings.png). Изображения используются как визуальный референс; отображённые там данные не являются тестом интеграций текущего Windows-порта.

## 4. Material 3 Expressive: точные опоры

В `theme/` вынесены Colors, Shape, Motion и Type. Это общие токены всей среды.

| Группа | Значения и поведение оригинала |
|---|---|
| Семантический цвет | primary/secondary/tertiary, пары on-*, контейнеры, error, outline, scrim, shadow, surfaceContainerLowest/Low/High/Highest и inverse; загружаются из theme.json |
| Радиусы | 0, 4, 8, 12, 16, 20, 28, 32, 48, full; выбор по роли компонента |
| Шрифты | Rubik; Material Symbols Rounded; JetBrainsMono Nerd Font |
| Типографика | display 57/36, headlines 28/24, title 22/16/14, body 16/14/12, label 14/12/11; веса и variable axes зависят от роли |
| Пространственная анимация | fast 367 мс, default 466 мс, slow 504 мс; заданные кривые с пружинным overshoot |
| Effects | 231 мс, монотонная кривая; цвет и прозрачность анимируются отдельно от перемещения |
| Живая пружина | SpringValue интегрирует target/value/velocity; damping/stiffness, подшаги до 4 мс и остановка после достижения epsilon |
| Состояния ввода | StateLayer: hover 8%, press 10% соответствующим on-* цветом; видимый клавиатурный focus — адаптация Windows; формы плиток, switches, segmented buttons, sliders и раскрытий согласованы |

Конкретная геометрия важна для переноса: quick tile высотой 64, зона перехода в детали шириной 44; шторка шириной 420 с прокруткой; настройки 960×680 (минимум 720×480), compact при ширине меньше 860, навигация 260/96. Switch 52×32 меняет thumb 16→24→28. Expressive Slider использует track 16, вертикальную ручку 44, ширину ручки 4→2 при перетаскивании и разрыв 6 с каждой стороны. Эти значения — логические размеры исходной реализации; Windows переводит их с учётом DPI, доступности и размеров экрана.

StateLayer не описывает самостоятельное состояние keyboard focus. Настройки шрифта запрашивают variable axes, но installer скачивает Rubik[wght].ttf: наличие ROND в выбранном Windows-файле и поведение WPF renderer требуется проверить, а не считать гарантированным.

Всплывающее окно визуально вырастает из исходного чипа; содержимое появляется каскадом. Жест закрытия учитывает расстояние и скорость. Есть морфинг фигур и волнистый прогресс. Равномерные скругления и один CubicEase на все переходы не воспроизводят эту систему.

В pinned-коде `nyri-wall` перечислена 21 схема геометрического стиля и 33 палитры (README всё ещё говорит про 20 стилей). `nyri-theme` вызывает matugen, поддерживает девять схем и светлый/тёмный режим, публикует theme.json атомарно. FileView наблюдает файл. Тема расходится в shell, compositor, GTK/Qt и терминалы; в Windows первая цель — согласованность собственных поверхностей Nyri, а внешние приложения требуют отдельных интеграций.

Источники: [Type.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/theme/Type.qml), [Shape.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/theme/Shape.qml), [Motion.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/theme/Motion.qml), [Colors.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/theme/Colors.qml), [nyri-theme](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/bin/nyri-theme), [nyri-wall](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/bin/nyri-wall).

## 5. Общая модель активностей

`Activities.qml` собирает реактивный список из состояния системных сервисов и внешних jobs. У активности есть id, kind, icon, tone, priority, title, text, progress, timestamps и actions. Одна модель обслуживает остров, шторку, live-popout и экран блокировки.

| Активность | Приоритет оригинала |
|---|---:|
| Запись экрана | 100 |
| Трансляция | 95 |
| Камера | 90 |
| Микрофон | 85 |
| Таймер | 80 |
| Секундомер | 78 |
| Jobs / копирование | 70 |
| Загрузка | 65 |
| Обновление | 60 |
| Музыка | 50 |
| VPN | 40 |
| Телефон | 32 |
| Bluetooth | 30 |

Ambient-активности не занимают постоянный приоритетный показ, но новое подключение временно становится заметным на 5 секунд. Клик открывает соответствующую страницу, а actions выполняют операцию провайдера. Таймеров может быть несколько, у них пауза и +1 минута; секундомер поддерживает паузу и круги.

Для Windows следует расширять IslandActivity совместимыми полями progress/tone/actions/provider state. Нынешние приоритеты Windows отличаются; их нельзя объявлять копией оригинала. Провайдеры и таймеры должен держать единый ShellRuntime, а окна подписываются на одну модель. Создание отдельного MediaSessionService/PrivacyService для каждой поверхности приведёт к дублированию подписок.

Источник: [Activities.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/services/Activities.qml).

## 6. Событийность и платформенные границы

Основная архитектура оригинала реактивная: события compositor/DBus, WirePlumber/MPRIS/BlueZ, watchers файлов и долгоживущие помощники с потоковым stdout. Фраза README «поллинга нет» не абсолютна для текущего дерева: есть периодические проверки клиентов Hyprland раз в 4 секунды, погоды раз в 30 минут и метрик раз в 2 секунды при наличии наблюдателей. Кроме того, есть enforcement блокировки микрофона раз в 3 секунды и проверка размеров активных загрузок раз в секунду. Таймеры, анимационные кадры и debounce отдельно не следует смешивать с постоянным опросом системы.

Для Windows предпочтительны нативные подписки; редкая сверка допустима как явно обозначенный fallback. Отсутствие событий или отказ доступа отображаются как неизвестное/недоступное состояние, а не фиктивное «всё выключено».

| Оригинальная зависимость | Windows-контракт / граница |
|---|---|
| Niri/Hypr IPC, focus, windows | EnumWindows для начального снимка + SetWinEventHook для событий; активный монитор и DPI — отдельный adapter |
| Layer-shell / exclusive zones | AppBar через SHAppBarMessage для панели и области работы; overlay/focus/hit testing — отдельные окна |
| Рабочие столы / scrolling tiling | Public IVirtualDesktopManager даёт desktop ID, проверку текущего и перенос окна; перечисление/создание/переключение и тайлинг этим API не обеспечены |
| MPRIS / WirePlumber | GSMTC для плееров; Core Audio callbacks для громкости/mute/сессий; EQ — самостоятельная аудиоинтеграция |
| NetworkManager / BlueZ | Native Wi-Fi и Bluetooth API; возможности, разрешения и список устройств проверяются в адаптере |
| UPower / brightnessctl | Windows power notifications, состояние батареи, доступный hardware brightness; монитор может не поддерживать управление |
| wl-clipboard / cliphist | Существующий ClipboardListener + отдельный ограниченный history store и действия |
| Privacy / /dev/video / PipeWire | Текущий ConsentStore watcher остаётся best effort; capture activity, system mute и управление разрешениями — разные состояния |
| DBus notifications | Собственные уведомления shell; чтение чужих уведомлений требует отдельной разрешённой интеграции, не является готовым поведением обычного EXE |
| KDE Connect | Выделенный phone adapter; существование Windows Phone Link не означает наличие доступного API для всего UI Nyri |
| grim/slurp/wf-recorder | Отдельный capture pipeline; проверять разрешения, поддерживаемую ОС, собственный процесс записи и фактическое окончание |
| WlSessionLock + PAM / polkit (CLI fallback также использует hyprlock) | Windows secure lock и системный UAC; WPF-экран не становится защищённым экраном входа |
| matugen / Python SVG generators | ThemeService + проверенный генератор цветов/обоев; оригинальная лицензия и лицензии встроенных shape/font assets сохраняются при заимствовании |

Проверенные первичные источники Windows: [AppBar API](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage), [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook), [IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager), [Core Audio volume callbacks](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolumecallback).

### Уточнения после чтения системных реализаций

- Privacy включает список приложений, историю последних 200 сессий микрофона/камеры/экрана, явную блокировку микрофона, режим privacy и скрытие чувствительных панелей от Niri screencast. Текущее Windows-обнаружение — только часть этого модуля.
- Камера в оригинале определяется по PipeWire video streams и helper nyri-camwatch. Helper ставит inotify на имеющиеся /dev/video*, затем по событиям сканирует /proc файловые дескрипторы. По этому коду hotplug нового video device не покрыт повторной регистрацией watches.
- File jobs принимает KDE JobView D-Bus protocol. Это сообщения поддерживающих этот protocol приложений; исходник не даёт глобального точного progress каждой операции копирования.
- Downloads наблюдает временные файлы .part/.crdownload/.download и другие суффиксы. Изменение размера и завершение определяются эвристически, общий размер загрузки неизвестен. Перенос должен сохранять честный indeterminate progress.
- VPN определяется по именам/типам интерфейсов и link events. Наличие интерфейса не подтверждает защищённость всего трафика.
- Состояние settings/widgets сохраняется; timers/stopwatch в Activities не записаны в persistent store, поэтому восстановление таймеров после выхода не следует приписывать оригиналу.
- Из скриптов установки видны пакеты Arch, links/backups, запуск сессии и greeter-интеграция. В pinned nyri-install обнаружен вызов undo_greeter в ветке --undo до определения функции (строки 13 и 98); при set -e это статически выглядит как ошибка отмены. Она не проверялась выполнением установщика. Windows installer должен проверяться отдельно от UI.

Источники: [Privacy.qml](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/config/quickshell/nyri/services/Privacy.qml), [nyri-camwatch](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/bin/nyri-camwatch), [nyri-jobs](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/bin/nyri-jobs), [nyri-install](https://github.com/yzewe/Nyri/blob/660cf73f7f8c6d6c8e563fff79c2a8e78aa33127/bin/nyri-install).

## 7. Разрыв с текущим NYRI-WIN10

Сейчас в Windows-коде есть отдельный Live Island и системные провайдеры, single-instance, позиция, media controls, clipboard, сеть/VPN, privacy-индикаторы и один взаимно исключающий таймер/секундомер без кругов и нескольких параллельных таймеров. Providers пока принадлежат MainWindow. Темизация — пять фиксированных brushes и Segoe UI; motion — общий переход 240 мс. Самостоятельных Bar/Dock/Launcher/ControlCenter/Desktop/Settings поверхностей нет.

Поэтому текущая сборка является фундаментом Windows-порта, а не готовой desktop-shell. Улучшение острова само по себе не закрывает основной запрос.

## 8. Следующие пачки разработки и проверяемые результаты

1. **ShellRuntime + дизайн-система.** Вынести существующие providers/lifecycle из MainWindow, сохранить ActivityHub и нынешнее поведение. Theme/Shape/Type/Motion tokens, общие UI-компоненты и router панелей; сохранить уже существующий Windows reduced motion. Проверка: одна подписка провайдера независимо от количества окон, startup/shutdown/reveal без утечек, существующие smoke tests проходят.
2. **Панель + launcher + dock.** Настраиваемая верхняя/нижняя панель с встроенным Live Island; настоящий индекс приложений и запуск; закрепления, список реальных окон, активация. Проверка: видимость на Windows 10/11, работа кликов и клавиатуры, AppBar cleanup, DPI/мониторы, перезапуск и сохранение состояния.
3. **Шторка + настройки.** Существующие media/timer/privacy/network работают через общую модель; Core Audio, батарея и доступные system toggles. Настройки отражают реальные capabilities. Проверка: изменения из Windows обновляют shell событиями, actions подтверждаются новым состоянием, unavailable не маскируется под off.
4. **Рабочий стол + wallpaper theme.** Начальные widgets часы/media/батарея/календарь, редактирование и сохранение раскладки; генератор обоев и синхронное обновление semantic roles. Проверка: обычные приложения остаются доступны, widgets не перехватывают весь ввод, положение восстанавливается, обе темы читаемы.
5. **History / notifications / OSD / window switcher.** Реальный history буфера, сообщения shell, действия, окна и overlay управления. Отдельные проверки permission-dependent интеграций.
6. **Расширенные адаптеры.** Bluetooth/phone, запись, разные мониторы/desktop strategies, EQ и session restore по отдельным проверенным пачкам. Контракты привязаны к доступным Windows API, а не к Linux-командам.

Минимальная shell-версия должна уже предоставлять панель, launcher, dock, шторку, рабочий стол и настройки в общей теме. Матрица паритета хранится отдельно от статуса «компилируется». Каждая значимая пачка — Release build, целевые проверки, commit/push main и успешный CI; подтверждение нового runtime проверяется отдельно от CI.

## 9. Лицензии и фиксация референса

Корневой код Nyri — MIT (yzewe, 2026). lib/shapes отдельно содержит Apache-2.0. При переносе исходного кода или существенных assets требуется сохранять соответствующие notices; шрифты рассматриваются отдельно по их файлам лицензий. В этом исследовании код и изображения оригинала не включены в исходники Windows-приложения. Reference clone лежит в ignored .tools/references/Nyri и не попадает в коммиты.
