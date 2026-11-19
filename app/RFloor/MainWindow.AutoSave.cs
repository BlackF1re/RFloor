using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private readonly DispatcherTimer autoSaveTimer=new();
    private readonly SemaphoreSlim saveGate=new(1);

    private void UpdateAutoSaveTimer()
    {
        autoSaveTimer.Stop();
        autoSaveTimer.Tick-=AutoSaveTick;
        if(!autoSaveEnabled)return;
        autoSaveTimer.Interval=TimeSpan.FromSeconds(autoSaveSeconds);
        autoSaveTimer.Tick+=AutoSaveTick;
        autoSaveTimer.Start();
    }

    private async void AutoSaveTick(object? sender,EventArgs e)=>await AutoSaveProjectsAsync();

    private bool CanAutoSaveProjects => autoSaveEnabled && measurementCancellation is null &&
        overlays.Count==0 && !switchingSession;

    private async Task AutoSaveProjectsAsync()
    {
        if(!CanAutoSaveProjects || !saveGate.Wait(0))return;
        try {
            foreach(var session in sessions.ToArray()) {
                if(!CanAutoSaveProjects)break;
                if(!sessions.Contains(session))continue;
                bool active=session==activeSession;
                string? target=active?path:session.Path;
                if(target is null || !(active?dirty:session.Dirty))continue;
                if(active ? inlineNameEditor is not null || !CommitDocumentSettings(reportError:false) : !session.CanAutoSave)continue;
                var file=storageFiles.GetValueOrDefault(target);
                string? local=file?.TryGetLocalPath();
                if(local is null && Uri.TryCreate(target,UriKind.Absolute,out var uri) && uri.IsFile)
                    local=uri.LocalPath;
                if(local is null && file is null)continue;
                var source=active?document:session.Document;
                var snapshot=source.DeepClone();
                try { await WriteProjectFileAsync(local,file,SerializeProject(snapshot)); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException) {
                    Status.Text=L("Не удалось выполнить автосохранение: {0}",ex.Message);
                    continue;
                }
                if(!sessions.Contains(session))continue;
                if(session==activeSession) {
                    if(path==target && ReferenceEquals(document,source) && CanAutoSaveProjects &&
                        inlineNameEditor is null && CommitDocumentSettings(reportError:false) && JsonNode.DeepEquals(source,snapshot))
                        dirty=false;
                } else if(session.Path==target && ReferenceEquals(session.Document,source) &&
                    session.CanAutoSave && JsonNode.DeepEquals(source,snapshot)) {
                    session.Dirty=false;
                }
                RefreshTabHeaders();
            }
        } finally { saveGate.Release(); }
    }

    private static string SerializeProject(JsonNode value) =>
        value.ToJsonString(new JsonSerializerOptions {WriteIndented=true})+Environment.NewLine;

    private static async Task WriteProjectFileAsync(string? local,IStorageFile? file,string json)
    {
        if(local is not null) {
            string temp=local+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                await File.WriteAllTextAsync(temp,json);
                File.Move(temp,local,true);
            } finally {
                if(File.Exists(temp))File.Delete(temp);
            }
            return;
        }

        await using var output=await file!.OpenWriteAsync();
        if(output.CanSeek)output.SetLength(0);
        await using var writer=new StreamWriter(output);
        await writer.WriteAsync(json);
    }
}
