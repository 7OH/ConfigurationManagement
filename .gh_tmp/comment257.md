Исправлено в **0.3.8.21** (и подтверждено в **0.3.8.22**).

Причина краха — включённая инвариантная глобализация (`InvariantGlobalization=true`), из-за которой на Windows/WPF падала активация любой привязки (`XmlLanguage.GetSpecificCulture()` → `InvalidOperationException: Cannot find non-neutral culture related to 'en-us'`) ещё на заголовке окна `MainWindow.xaml`.

Решение: флаг `InvariantGlobalization` выключен в `Configuration Management.csproj`, а также убран из скриптов сборки single-file (`build-windows-single-file.ps1`, `build-linux-single-file.ps1`, `build-linux-single-file.sh`). Замеры показали, что флаг не давал экономии размера (разница ~59 байт), поэтому его отключение безопасно.

Сопутствующий `NullReferenceException` в `MainWindow.OnClosing` возникал из-за незавершённого конструктора окна и исчезает вместе с устранением корневой причины.