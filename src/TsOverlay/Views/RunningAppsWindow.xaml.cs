using System.Diagnostics;
using System.Windows;

namespace TsOverlay.Views;

public partial class RunningAppsWindow : Window
{
    public RunningAppsWindow()
    {
        InitializeComponent();
        Refresh();
    }

    public IReadOnlyList<ProcessItem> SelectedProcesses { get; private set; } = Array.Empty<ProcessItem>();

    private void Refresh()
    {
        var items = new List<ProcessItem>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                var title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                if (process.ProcessName.Equals("TsOverlay", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(new ProcessItem(process.ProcessName, title));
            }
            catch
            {
                // некоторые процессы недоступны — пропускаем
            }
            finally
            {
                process.Dispose();
            }
        }

        AppsList.ItemsSource = items
            .GroupBy(i => i.Name + "|" + i.Title)
            .Select(g => g.First())
            .OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        SelectedProcesses = AppsList.SelectedItems.Cast<ProcessItem>().ToList();
        DialogResult = true;
        Close();
    }

    public sealed record ProcessItem(string Name, string Title);
}
