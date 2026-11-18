using System.Text.Json.Nodes;

namespace RFloor;
public partial class MainWindow
{
    private void MeasurementSettings_Click(object sender,RoutedEventArgs e)=>ShowPreferences(1);
    private async void ShowPreferences(int selectedTab)
    {
        if(measurementCancellation is not null)return;
        CommitInlineRename();
        TextBox[] fields=[PingTargets,PingCount,IperfHost,IperfPort,IperfDuration,NetworkRefresh];
        CheckBox[] checks=[RunSignal,RunPing,RunIperf,RunUpload];
        string[] texts=fields.Select(x=>x.Text).ToArray();bool?[] states=checks.Select(x=>x.IsChecked).ToArray();
        var parent=(Panel)MeasurementSettingsPanel.Parent;int index=parent.Children.IndexOf(MeasurementSettingsPanel);
        bool previousLoading=loadingSettings,accepted=false;loadingSettings=true;
        parent.Children.Remove(MeasurementSettingsPanel);MeasurementSettingsPanel.IsVisible=true;
        var tabs=new TabControl {Classes={"settingsTabs"},HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,Margin=new Thickness(16,16,16,0)};
        ScrollViewer Wrap(UIElement content)=>new(){Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(18)};
        void Tab(string title,UIElement content)=>tabs.Items.Add(new TabItem {Header=title,Content=Wrap(content)});
        TextBlock Note(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray,Margin=new Thickness(0,6,0,16)};
        var editor=new StackPanel();editor.Children.Add(new TextBlock {Text=L("Холст и навигация"),FontSize=20,FontWeight=FontWeights.SemiBold});
        editor.Children.Add(new TextBlock {Text=L("Язык интерфейса"),Margin=new Thickness(0,12,0,4)});
        var language=new ComboBox {ItemsSource=Localization.AvailableLanguages,
            SelectedItem=Localization.AvailableLanguages.Single(pack=>pack.Code==Localization.Language),
            Margin=new Thickness(0,0,0,12),Padding=new Thickness(6)};
        editor.Children.Add(language);
        editor.Children.Add(Note(L("Настройки отображения сохраняются для приложения. Привязку можно временно отключить клавишей Shift. Режим линеек переключается также кнопкой в углу холста.")));
        var grid=new CheckBox {Content=L("Показывать сетку"),IsChecked=showGrid,Margin=new Thickness(0,0,0,12)};
        var names=new CheckBox {Content=L("Показывать подписи объектов"),IsChecked=showObjectLabels,Margin=new Thickness(0,0,0,12)};
        var snap=new CheckBox {Content=L("Привязка к геометрии и сетке"),IsChecked=SnapEnabled.IsChecked,Margin=new Thickness(0,0,0,12)};
        editor.Children.Add(grid);editor.Children.Add(names);editor.Children.Add(snap);
        editor.Children.Add(new TextBlock {Text=L("Непрозрачность подписей")});
        var opacity=new Slider {Minimum=.2,Maximum=1,Value=labelOpacity,TickFrequency=.05,IsSnapToTickEnabled=true,Margin=new Thickness(0,10,0,18)};editor.Children.Add(opacity);
        var opacityValue=new TextBlock {Foreground=Brushes.Gray,Margin=new Thickness(0,0,0,12)};
        void OpacityText()=>opacityValue.Text=$"{opacity.Value*100:0} %";OpacityText();opacity.ValueChanged+=(_,_)=>OpacityText();editor.Children.Add(opacityValue);
        editor.Children.Add(new TextBlock {Text=L("Шаг масштабирования колёсиком")});
        var zoom=new Slider {Minimum=1.05,Maximum=1.5,Value=zoomFactor,TickFrequency=.05,IsSnapToTickEnabled=true,Margin=new Thickness(0,10,0,8)};editor.Children.Add(zoom);
        var zoomValue=new TextBlock {Foreground=Brushes.Gray,Margin=new Thickness(0,0,0,18)};
        void ZoomText()=>zoomValue.Text=L("{0:0} % за шаг", (zoom.Value-1)*100);ZoomText();zoom.ValueChanged+=(_,_)=>ZoomText();editor.Children.Add(zoomValue);
        editor.Children.Add(new TextBlock {Text=L("Линейки")});
        var rulers=new ComboBox {ItemsSource=new[]{L("Без линеек"),L("Риски на линейках"),L("Риски и проекция курсора")},SelectedIndex=rulerMode,Margin=new Thickness(0,6,0,12),Padding=new Thickness(6)};editor.Children.Add(rulers);
        editor.Children.Add(Note(L("Колесо — вертикальная прокрутка; Shift+колесо — горизонтальная; Ctrl+колесо — масштабирование относительно курсора. Ctrl+Tab переключает документы.")));Tab(L("Редактор"),editor);
        Tab(L("Измерения"),MeasurementSettingsPanel);
        var thresholds=new StackPanel();thresholds.Children.Add(new TextBlock {Text=L("Цвета результатов"),FontSize=20,FontWeight=FontWeights.SemiBold});thresholds.Children.Add(Note(L("Три значения: плохой (красный), средний (жёлтый), хороший (ярко-зелёный). Вне покрытия заливка отсутствует. Пороги и параметры измерений сохраняются в текущем документе.")));
        var thresholdFields=new List<(string Key,TextBox[] Boxes)>();
        var source=document["settings"] as JsonObject ?? document["planSettings"] as JsonObject;
        var headings=new UniformGrid {Columns=3};foreach(string title in new[]{L("Плохо"),L("Средне"),L("Хорошо")})headings.Children.Add(new TextBlock {Text=title,FontWeight=FontWeights.SemiBold});thresholds.Children.Add(headings);
        foreach(var row in ThresholdDefaults){
            thresholds.Children.Add(new TextBlock {Text=L(row.Label),Margin=new Thickness(0,10,0,2)});
            var boxes=new TextBox[3];var values=source?["thresholds"]?[row.Key] as JsonArray;var columns=new UniformGrid {Columns=3};
            for(int i=0;i<3;i++){boxes[i]=new TextBox {Text=Display(values is {Count:3} && Numeric(values[i],out double n)?n:row.Values[i]),Tip=new[]{L("Плохо"),L("Средне"),L("Хорошо")}[i],Margin=new Thickness(0,2,10,4)};columns.Children.Add(boxes[i]);}
            thresholdFields.Add((row.Key,boxes));thresholds.Children.Add(columns);
        }Tab(L("Пороги"),thresholds);
        var bindings=new StackPanel();bindings.Children.Add(new TextBlock {Text=L("Сочетания клавиш"),FontSize=20,FontWeight=FontWeights.SemiBold});bindings.Children.Add(Note(L("Выберите поле и нажмите нужное сочетание. Повторяющиеся сочетания не допускаются. Стандартные команды редактирования текста действуют внутри полей ввода.")));
        var shortcutFields=new Dictionary<string,TextBox>();
        foreach(var row in ShortcutDefaults){
            var line=new Grid {Margin=new Thickness(0,0,0,4)};line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(compactLayout?120:210)});
            line.Children.Add(new TextBlock {Text=L(row.Label),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,12,0)});
            var box=new TextBox {Text=shortcuts[row.Id],IsReadOnly=true,Margin=new Thickness(0,2,0,2),Tip=L("Нажмите сочетание клавиш")};Grid.SetColumn(box,1);line.Children.Add(box);shortcutFields[row.Id]=box;
            box.KeyDown+=(_,e)=>{Key key=e.Key==Key.None?e.Key:e.Key;if(key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)){box.Text=Chord(key,PointerContext.Modifiers);box.CaretIndex=box.Text.Length;}e.Handled=true;};bindings.Children.Add(line);
        }
        var reset=new Button {Content=L("Восстановить стандартные сочетания"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0)};
        reset.Click+=(_,_)=>{foreach(var row in ShortcutDefaults)shortcutFields[row.Id].Text=row.Gesture;};bindings.Children.Add(reset);Tab(L("Клавиши"),bindings);
        tabs.SelectedIndex=selectedTab;
        var error=new TextBlock {Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(20,12,20,0)};
        var apply=new Button {Content=L("Применить"),MinWidth=110,IsDefault=true};var cancel=new Button {Content=L("Отмена"),MinWidth=90,IsCancel=true};
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(20,12,20,16)};buttons.Children.Add(cancel);buttons.Children.Add(apply);apply.Margin=new Thickness(8,0,0,0);
        var footer=new StackPanel();footer.Children.Add(error);footer.Children.Add(buttons);DockPanel.SetDock(footer,Dock.Bottom);
        var root=new DockPanel();root.Children.Add(footer);root.Children.Add(tabs);
        var dialog=new OverlayDialog(this) {Title=L("Настройки RFloor"),Width=780,Height=Math.Min(780,900-50),MinWidth=0,MinHeight=0,Content=root,Background=Brushes.White};
        cancel.Click+=(_,_)=>dialog.Cancel();
        apply.Click+=(_,_)=>{
            try {
                var options=ReadMeasurementOptions();if(!int.TryParse(NetworkRefresh.Text,out int interval)||interval is <2 or >120)throw new FormatException(L("Интервал обновления Wi-Fi: от 2 до 120 секунд."));
                var colors=new JsonObject();foreach(var (key,boxes) in thresholdFields){var values=boxes.Select(b=>Parse(b,L("Порог"))).ToArray();if(!(key is "ping" or "loss"?values[0]>values[1]&&values[1]>values[2]:values[0]<values[1]&&values[1]<values[2]))throw new FormatException(L("Пороги должны идти от плохого результата к хорошему."));if(key=="loss" && values.Any(x=>x<0||x>100)||key=="quality" && values.Any(x=>x<0||x>100)||key is "ping" or "download" or "upload" && values.Any(x=>x<0))throw new FormatException(L("Проверьте диапазон порогов: проценты 0–100, задержка и скорость неотрицательны."));colors[key]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());}
                var changedBindings=shortcutFields.ToDictionary(x=>x.Key,x=>NormalizeShortcut(x.Value.Text));
                var duplicate=changedBindings.GroupBy(x=>x.Value).FirstOrDefault(x=>x.Count()>1);if(duplicate is not null)throw new FormatException(L("Сочетание ")+duplicate.Key+L(" назначено нескольким командам."));
                Remember();var settings=document["settings"] as JsonObject ?? document["planSettings"]!.AsObject();
                var measurementSettings=settings;
                measurementSettings["runSignal"]=options.Signal;measurementSettings["runPing"]=options.Ping;measurementSettings["runIperf"]=options.Download;measurementSettings["runUpload"]=options.Upload;
                measurementSettings["pingTargets"]=new JsonArray(options.Targets.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());measurementSettings["pingCount"]=options.Count;measurementSettings["iperfHost"]=options.Host;measurementSettings["iperfPort"]=options.Port;measurementSettings["iperfDuration"]=options.Duration;measurementSettings["networkRefreshSeconds"]=interval;
                if(!ReferenceEquals(settings,measurementSettings))settings["surveyDefaults"]=measurementSettings;
                settings["thresholds"]=colors;networkTimer.Interval=TimeSpan.FromSeconds(interval);
                showGrid=grid.IsChecked==true;showObjectLabels=names.IsChecked==true;SnapEnabled.IsChecked=snap.IsChecked;labelOpacity=opacity.Value;zoomFactor=zoom.Value;rulerMode=rulers.SelectedIndex;shortcuts=changedBindings;
                if (language.SelectedItem is Localization.LanguagePack pack) Localization.SetLanguage(pack.Code);
                SavePreferences();UpdateShortcutHints();dirty=true;accepted=true;dialog.Complete(true);
            }catch(FormatException ex){error.Text=ex.Message;}
        };
        try{await dialog.ShowAsync();}
        finally {
            if(!accepted){for(int i=0;i<fields.Length;i++)fields[i].Text=texts[i];for(int i=0;i<checks.Length;i++)checks[i].IsChecked=states[i];}
            ((ScrollViewer)((TabItem)tabs.Items[1]).Content).Content=null;MeasurementSettingsPanel.IsVisible=false;parent.Children.Insert(index,MeasurementSettingsPanel);loadingSettings=previousLoading;
        }
        SwitchMode(surveyMode);RenderPlan();RefreshRulers();RefreshTabHeaders();
        RefreshPanelButtons();
        foreach(var session in sessions)if(session.Tab.Header is StackPanel header)
            foreach(var button in header.Children.OfType<Button>())button.Tip=L("Закрыть вкладку (Ctrl+W)");
        UpdateGridNumbers();
    }
}
