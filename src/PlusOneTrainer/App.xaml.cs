using System.Windows;
using PlusOneTrainer.Services;

namespace PlusOneTrainer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalizationService.ApplySavedLanguage();
    }
}
