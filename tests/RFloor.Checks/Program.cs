using Avalonia;
using Avalonia.Headless;
using RFloor;
internal static class Program {
 [STAThread] private static int Main(){
  try {NetworkChecks.Run().GetAwaiter().GetResult();
   AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions {UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
   CoverageChecks.Run();LocalizationChecks.Run();var view=new MainWindow();EditorLogic.Run(view);UnifiedDocument.Run();LayoutChecks.Run();PhoneChecks.Run();Console.WriteLine("Shared application checks passed.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
