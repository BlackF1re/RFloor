using System.IO;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private sealed class ProjectSession(JsonObject document, string? path)
    {
        public JsonObject Document=document;
        public string? Path=path;
        public bool Dirty;
        public bool Survey=path is not null && document["points"] is JsonArray points &&
            points.OfType<JsonObject>().SelectMany(PointReadings).Any(reading=>reading["measurements"] is JsonObject {Count:>0});
        public string? Search;
        public string[] Undo=[], Redo=[], Selection=[];
        public Dictionary<string,string> Drafts=[];
        public bool Snap=true, Heat=true;
        public int Grid=1, Band=1, Metric, Rulers=1;
        public string? AccessPoint;
        public double Zoom=1, X, Y;
        public Thickness Padding;
        public string Tool="select";
        public readonly TabItem Tab=new();
        public readonly TextBlock Header=new() {VerticalAlignment=VerticalAlignment.Center,MaxWidth=230,TextTrimming=TextTrimming.CharacterEllipsis};
    }
    private readonly List<ProjectSession> sessions=[];
    private ProjectSession? activeSession;
    private bool switchingSession;
    private static readonly string[] DraftNames=["RoomName","RoomX","RoomY","RoomWidth","RoomHeight","Thickness","DoorWidth","ProjectName","PingTargets","PingCount","IperfHost","IperfPort","IperfDuration","NetworkRefresh"];
    private void InitializeWorkspace()
    {
        LoadPreferences();
        InitializeStartScreen();
    }
    private void CaptureSession()
    {
        if(activeSession is not {} session)return;
        CommitInlineRename();
        session.Document=document;session.Path=path;session.Dirty=dirty;session.Survey=surveyMode;
        session.Undo=undo.ToArray();session.Redo=redo.ToArray();
        session.Drafts=DraftNames.ToDictionary(n=>n,n=>((TextBox)FindName(n)).Text);
        foreach(var (name,check) in new[]{("RunSignal",RunSignal),("RunPing",RunPing),("RunIperf",RunIperf),("RunUpload",RunUpload)})session.Drafts[name]=check.IsChecked==true?"true":"false";
        session.Snap=SnapEnabled.IsChecked==true;session.Heat=ShowHeat.IsChecked==true;
        session.Grid=GridPicker.SelectedIndex;session.Band=BandPicker.SelectedIndex;session.Metric=MetricPicker.SelectedIndex;session.AccessPoint=SelectedAccessPoint;
        session.Zoom=PlanZoom.ScaleX;session.Padding=PlanSurface.Padding;session.X=Viewport.HorizontalOffset;session.Y=Viewport.VerticalOffset;session.Rulers=rulerMode;session.Tool=tool;
        session.Selection=Objects.SelectedItems.Cast<ObjectEntry>().Select(x=>x.Item["id"]?.ToString()??"").ToArray();
        session.Search=ObjectSearch.Text;
    }
    private void AddDocument(JsonObject doc,string? file=null)
    {
        if(measurementCancellation is not null)return;
        if(file is not null && sessions.FirstOrDefault(s=>string.Equals(s==activeSession?path:s.Path,file,StringComparison.OrdinalIgnoreCase)) is {} opened){ActivateSession(opened);return;}
        NormalizeProject(doc); Welcome.IsVisible=false; Workspace.IsEnabled=true; Workspace.Opacity=1;
        CaptureSession();
        var session=new ProjectSession(doc,file) {Rulers=rulerMode, Snap=SnapEnabled.IsChecked==true};
        var header=new StackPanel {Orientation=Orientation.Horizontal};header.Children.Add(session.Header);
        var close=new Button {Content="×",Padding=new Thickness(5,0,5,1),Margin=new Thickness(10,0,0,0),Background=Brushes.Transparent,Tip=L("Закрыть вкладку (Ctrl+W)"),Focusable=false};
        close.Click+=(_,e)=>{e.Handled=true;CloseSession(session);};header.Children.Add(close);
        session.Header .PointerPressed+=(_,e)=>{if(e.Clicks()!=2)return;e.Handled=true;ActivateSession(session);var input=new TextBox {Text=ProjectName.Text,MinWidth=150};header.Children.Remove(session.Header);header.Children.Insert(0,input);bool finished=false;void Finish(bool accept){if(finished)return;finished=true;if(accept && !string.IsNullOrWhiteSpace(input.Text) && input.Text.Trim()!=ProjectName.Text){Remember();document["name"]=input.Text.Trim();loadingSettings=true;ProjectName.Text=input.Text.Trim();loadingSettings=false;dirty=true;}header.Children.Remove(input);header.Children.Insert(0,session.Header);RefreshTabHeaders();}input.KeyDown+=(_,k)=>{if(k.Key==Avalonia.Input.Key.Enter){Finish(true);k.Handled=true;}else if(k.Key==Avalonia.Input.Key.Escape){Finish(false);k.Handled=true;}};input.LostFocus+=(_,_)=>Finish(true);input.Focus();input.SelectAll();};
        session.Tab.Header=header;session.Tab.Tag=session;
        sessions.Add(session);switchingSession=true;DocumentTabs.Items.Add(session.Tab);switchingSession=false;
        ActivateSession(session);
    }
    private void ActivateSession(ProjectSession session)
    {
        if(measurementCancellation is not null || activeSession==session)return;
        CaptureSession();switchingSession=true;
        try {
            ResetPointerTools();Objects.SelectedItems.Clear();activeSession=session;
            document=session.Document;path=session.Path;dirty=session.Dirty;
            undo.Clear();redo.Clear();foreach(var value in session.Undo.Reverse())undo.Push(value);foreach(var value in session.Redo.Reverse())redo.Push(value);
            LoadDocumentSettings();loadingSettings=true;
            try {
                if(session.Drafts.Count==0) {
                    RoomName.Text=L("Комната");RoomX.Text=RoomY.Text="0";RoomWidth.Text="4";RoomHeight.Text="3";
                }
                foreach(var (name,value) in session.Drafts) {
                    if(FindName(name) is TextBox field)field.Text=value;
                    else if(FindName(name) is CheckBox check)check.IsChecked=value=="true";
                }
                SnapEnabled.IsChecked=session.Snap;ShowHeat.IsChecked=session.Heat;
                if(session.Drafts.Count>0)GridPicker.SelectedIndex=session.Grid;
                BandPicker.SelectedIndex=session.Band;MetricPicker.SelectedIndex=session.Metric;
            } finally {loadingSettings=false;}
            origin=50;PlanSurface.Padding=session.Padding;PlanZoom.ScaleX=PlanZoom.ScaleY=session.Zoom;rulerMode=session.Rulers;
            ObjectSearch.Text=session.Search;
            tool=session.Tool;SwitchMode(session.Survey);SetToolVisuals();
            Objects.SelectedItems.Clear();foreach(var entry in Objects.Items.Cast<ObjectEntry>().Where(e=>session.Selection.Contains(e.Item["id"]?.ToString())))Objects.SelectedItems.Add(entry);
            if(session.AccessPoint is not null)AccessPointPicker.SelectedItem=AccessPointPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(x=>x.Tag as string==session.AccessPoint);
            Viewport.UpdateLayout();Viewport.ScrollToHorizontalOffset(session.X);Viewport.ScrollToVerticalOffset(session.Y);
            ZoomLabel.Text=$"{session.Zoom*100:0} %";DocumentTabs.SelectedItem=session.Tab;
            RefreshTabHeaders();RefreshRulers();
        } finally {switchingSession=false;}
    }
    private void DocumentTabs_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!switchingSession && ReferenceEquals(e.Source,DocumentTabs) && DocumentTabs.SelectedItem is TabItem {Tag:ProjectSession session})ActivateSession(session);
    }
    private void RefreshTabHeaders()
    {
        foreach(var session in sessions) {
            var doc=session==activeSession?document:session.Document;string? file=session==activeSession?path:session.Path;
            bool modified=session==activeSession?dirty:session.Dirty;
            string extension=".rfplan";
            session.Header.Text=(file is null ? (doc["name"]?.ToString()??L("Новый план"))+extension : System.IO.Path.GetFileName(file))+(modified?" *":"");
            session.Tab.Tip=file??session.Header.Text;
        }
        if(activeSession is not null)Title=activeSession.Header.Text+" — RFloor";
    }
    private async void CloseSession(ProjectSession session)
    {
        if(measurementCancellation is not null)return;
        ActivateSession(session);CommitInlineRename();if(!await ConfirmDiscard())return;
        int index=sessions.IndexOf(session);activeSession=null;switchingSession=true;sessions.Remove(session);DocumentTabs.Items.Remove(session.Tab);switchingSession=false;
        if(sessions.Count==0)ShowWelcome();else ActivateSession(sessions[Math.Min(index,sessions.Count-1)]);
    }
    private void CloseTab_Click(object sender,RoutedEventArgs e) {if(activeSession is {} session)CloseSession(session);}
    private void NewSurvey_Click(object sender,RoutedEventArgs e)
    {
        if(measurementCancellation is not null)return;
        SwitchMode(true);
    }
    private void SetToolVisuals()
    {
        foreach(var button in new[]{SelectTool,RoomTool,WallTool,DoorTool,TapeTool,RectangleTool,RoundedRectangleTool,RouterTool,EllipseTool,LineTool,TextTool})button.Background=new SolidColorBrush((Color)Color.Parse(!surveyMode && (string)button.Tag==tool?"#FFE5CC":"#F2F2F2"));
    }
}
