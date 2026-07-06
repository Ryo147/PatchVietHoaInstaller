using System.Windows;

namespace VietHoaInstaller
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SystemParameters.StaticPropertyChanged += (s, args) => { }; // no-op, tránh warning unused
        }
    }
}