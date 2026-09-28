Исправлено в версии **0.3.9.118** (Windows/WPF и Linux/Avalonia).

**Что было не так.** По замечанию автора issue кнопка копирования стояла у поля
«Наименование» (после правки 0.3.9.112) и копировала значение «Имя базы на
сервере» в «Наименование». Требование автора: источник — «Имя базы на сервере»
(RefBox), приёмник — «Имя базы данных» (DbNameBox), а поле «Наименование»
вообще не задействовать.

**Как исправлено.** Кнопка перенесена к полю «Имя базы данных»; клик по кнопке
копирует значение из «Имя базы на сервере» в «Имя базы данных». У поля
«Наименование» кнопки больше нет, само поле не изменяется. Пустое значение Ref
имя базы данных не затирает (поведение сохранено).

- Windows/WPF — [`Views/CreateInfobaseWindow.xaml`](Configuration%20Management/Views/CreateInfobaseWindow.xaml):
  `NameBox` возвращён к простому TextBox, `DbNameBox` обёрнут в Grid
  (TextBox + кнопка-иконка ContentCopy, ToolTip `CreateInfobase.CopyRefToDbName`);
  обработчик `OnCopyRefToName_Click` переименован в `OnCopyRefToDbName_Click`
  (Ref → DbName, пустой Ref не затирает приёмник).
- Linux/Avalonia — [`Views/CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs):
  кнопка `copyRefToDb` собрана у строки `_dbNameBox`, у `_nameBox` убрана;
  `CopyRefToName()` переименована в `CopyRefToDbName()` (Ref → DbName).
- Локализация: ключ `CreateInfobase.CopyRefToName` заменён на
  `CreateInfobase.CopyRefToDbName` («Скопировать имя базы на сервере в имя базы
  данных» / "Copy the server base name to the database name") в ru.json и en.json.

**Как проверить.**

1. Установите версию **0.3.9.118** (Windows или Linux).
2. Откройте окно создания ИБ и выберите тип «Клиент-серверная».
3. Заполните «Имя базы на сервере», затем нажмите кнопку-иконку рядом с полем
   «Имя базы данных» — значение из «Имя базы на сервере» появится в «Имя базы данных».
4. Очистите «Имя базы на сервере» и снова нажмите кнопку — «Имя базы данных»
   не изменится (пустой Ref не затирает приёмник).
5. Убедитесь, что у поля «Наименование» кнопки больше нет и его значение не меняется.