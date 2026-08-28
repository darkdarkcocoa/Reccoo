using System.Windows;

namespace CocoaRecorder;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // MainWindow가 만들어지기 전에 언어 리소스를 채워 넣는다.
        L10n.Init();
    }
}
