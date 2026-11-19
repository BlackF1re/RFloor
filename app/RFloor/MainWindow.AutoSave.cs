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
                var snapshot=source.DeepClone().AsObject();
                var modifiedAt=SaveTimestamp(snapshot,true);
                try { await WriteProjectFileAsync(local,file,SerializeProject(snapshot,modifiedAt)); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException) {
                    Status.Text=L("Не удалось выполнить автосохранение: {0}",ex.Message);
                    continue;
                }
                if(!sessions.Contains(session))continue;
                if(session==activeSession) {
                    if(path==target && ReferenceEquals(document,source) && CanAutoSaveProjects &&
                        inlineNameEditor is null)
                        FinishSave(source,snapshot,modifiedAt);
                } else if(session.Path==target && ReferenceEquals(session.Document,source) &&
                    session.CanAutoSave && JsonNode.DeepEquals(source,snapshot)) {
                    session.Dirty=false;
                    source["modifiedAt"]=modifiedAt;
                }
                RefreshTabHeaders();
            }
        } finally { saveGate.Release(); }
    }

    private static string SaveTimestamp(JsonObject project,bool changed) =>
        !changed && project["modifiedAt"] is {} saved?saved.ToString():DateTime.UtcNow.ToString("O");

    private static string SerializeProject(JsonObject project,string modifiedAt)
    {
        var header=new[]{"format","schema","createdAt","modifiedAt","name","devices","settings"};
        var ordered=new JsonObject();
        foreach(var key in header)
            if(key=="modifiedAt") ordered[key]=modifiedAt;
            else if(project.ContainsKey(key)) ordered[key]=project[key]?.DeepClone();
        foreach(var field in project)
            if(!ordered.ContainsKey(field.Key)) ordered[field.Key]=field.Value?.DeepClone();
        return ordered.ToJsonString(new JsonSerializerOptions {WriteIndented=true})+Environment.NewLine;
    }

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
