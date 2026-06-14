using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using StorageChecker.App.ViewModels;

namespace StorageChecker.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadWindowIcon();
        InitializeCharts();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
            oldVm.Dashboard.PropertyChanged -= Dashboard_PropertyChanged;
        if (e.NewValue is MainViewModel newVm)
            newVm.Dashboard.PropertyChanged += Dashboard_PropertyChanged;
    }

    private void Dashboard_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.DailyDates)
                         or nameof(DashboardViewModel.DailyAdded)
                         or nameof(DashboardViewModel.CategoryLabels))
        {
            Dispatcher.BeginInvoke(RefreshDashboardCharts);
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedIndex == 2)
            RefreshDashboardCharts();
    }

    private void InitializeCharts()
    {
        void Style(ScottPlot.WPF.WpfPlot plot, string title, string xLabel, string yLabel)
        {
            plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
            plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#FAFAFA");
            plot.Plot.Title(title);
            plot.Plot.XLabel(xLabel);
            plot.Plot.YLabel(yLabel);
            plot.Plot.Axes.Left.MajorTickStyle.Length = 6;
            plot.Plot.Axes.Bottom.MajorTickStyle.Length = 6;
            plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#E0E0E0");
        }

        Style(DailyChart, "Tambah vs Hapus per Hari", "Tanggal", "Bytes");
        Style(NetChart, "Net Change Trend", "Tanggal", "Net Bytes");
        Style(CategoryChart, "Breakdown per Kategori", "Bytes", "Kategori");
    }

    private void RefreshDashboardCharts()
    {
        if (DataContext is not MainViewModel vm) return;
        var db = vm.Dashboard;

        RefreshDailyChart(db);
        RefreshNetChart(db);
        RefreshCategoryChart(db);
    }

    private void RefreshDailyChart(DashboardViewModel db)
    {
        DailyChart.Plot.Clear();
        int n = db.DailyDates.Length;
        if (n > 0)
        {
            double[] positions = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
            const double width = 0.35;
            string[] labels = db.DailyDates.Select(d => d.ToString("dd/MM")).ToArray();

            var addedBars = Enumerable.Range(0, n)
                .Select(i => new ScottPlot.Bar
                {
                    Position = positions[i] - width / 2,
                    Value = db.DailyAdded[i],
                    Size = width,
                    FillColor = new ScottPlot.Color(76, 175, 80)
                }).ToArray();
            foreach (var bar in addedBars)
                DailyChart.Plot.Add.Bar(bar);

            var deletedBars = Enumerable.Range(0, n)
                .Select(i => new ScottPlot.Bar
                {
                    Position = positions[i] + width / 2,
                    Value = db.DailyDeleted[i],
                    Size = width,
                    FillColor = new ScottPlot.Color(33, 150, 243)
                }).ToArray();
            foreach (var bar in deletedBars)
                DailyChart.Plot.Add.Bar(bar);

            DailyChart.Plot.Legend.IsVisible = true;
            DailyChart.Plot.Legend.Alignment = ScottPlot.Alignment.UpperRight;
            DailyChart.Plot.Axes.Bottom.SetTicks(positions, labels);
        }
        DailyChart.Plot.Axes.AutoScale();
        DailyChart.Refresh();
    }

    private void RefreshNetChart(DashboardViewModel db)
    {
        NetChart.Plot.Clear();
        int n = db.DailyDates.Length;
        if (n > 0)
        {
            double[] xs = db.DailyDates.Select(d => d.ToOADate()).ToArray();
            var scatter = NetChart.Plot.Add.Scatter(xs, db.DailyNet);
            scatter.Color = new ScottPlot.Color(255, 152, 0);
            scatter.LineWidth = 2;
            scatter.MarkerSize = 6;
            NetChart.Plot.Axes.DateTimeTicksBottom();
        }
        NetChart.Plot.Axes.AutoScale();
        NetChart.Refresh();
    }

    private void RefreshCategoryChart(DashboardViewModel db)
    {
        CategoryChart.Plot.Clear();
        int m = db.CategoryLabels.Length;
        if (m > 0)
        {
            double[] positions = Enumerable.Range(0, m).Select(i => (double)i).ToArray();
            double[] totals = db.CategoryAdded.Zip(db.CategoryDeleted, (a, d) => a + d).ToArray();
            string[] labels = db.CategoryLabels.ToArray();

            var bars = Enumerable.Range(0, m)
                .Select(i => new ScottPlot.Bar
                {
                    Position = positions[i],
                    Value = totals[i],
                    Size = 0.6,
                    FillColor = ScottPlot.Colors.Category10[i % ScottPlot.Colors.Category10.Length]
                }).ToArray();
            foreach (var bar in bars)
                CategoryChart.Plot.Add.Bar(bar);
            CategoryChart.Plot.Axes.Left.SetTicks(positions, labels);
            CategoryChart.Plot.Axes.Left.MajorTickStyle.Length = 0;
        }
        CategoryChart.Plot.Axes.AutoScale();
        CategoryChart.Refresh();
    }

    private void LoadWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                Icon = BitmapFrame.Create(
                    new Uri(iconPath, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
            }
        }
        catch
        {
            // Abaikan — icon jendela boleh kosong, aplikasi tetap jalan.
        }
    }
}