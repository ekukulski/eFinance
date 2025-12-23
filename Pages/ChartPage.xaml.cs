using Microsoft.Maui.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using KukiFinance.Services;

namespace KukiFinance.Pages
{
    public partial class ChartPage : ContentPage
    {
        public ChartPage(
            string account,
            int year,
            List<(DateTime WeekEndDate, decimal Balance)> actualBalances,
            List<(DateTime WeekEndDate, decimal Balance)> forecastBalances)
        {
            InitializeComponent();
            WindowCenteringService.CenterWindow(2750, 600);

            TitleLabel.Text = $"{account} - {year} Weekly Balance";

            // Start at first Saturday of selected year
            DateTime startDate = new DateTime(year, 1, 1);
            DateTime firstSaturday = startDate;
            while (firstSaturday.DayOfWeek != DayOfWeek.Saturday)
                firstSaturday = firstSaturday.AddDays(1);

            // End at the Saturday on or after one year from today
            DateTime endDate = DateTime.Today.AddYears(1);
            DateTime lastSaturday = endDate;
            while (lastSaturday.DayOfWeek != DayOfWeek.Saturday)
                lastSaturday = lastSaturday.AddDays(1);

            // Build the list of Saturdays
            var saturdays = new List<DateTime>();
            for (var dt = firstSaturday; dt <= lastSaturday; dt = dt.AddDays(7))
                saturdays.Add(dt);

            var actualDict = actualBalances.ToDictionary(b => b.WeekEndDate, b => (double)b.Balance);
            var forecastDict = forecastBalances.ToDictionary(b => b.WeekEndDate, b => (double)b.Balance);

            // Find the last actual Saturday (≤ today)
            var lastActualSaturday = saturdays.Where(d => d <= DateTime.Today && actualDict.ContainsKey(d)).LastOrDefault();
            var lastActualBalance = lastActualSaturday != default ? actualDict[lastActualSaturday] : double.NaN;

            // Find the first Saturday after today
            var firstForecastSaturday = saturdays.FirstOrDefault(d => d > DateTime.Today);

            // Build line values: actuals up to today, then connect to forecast, repeat last known forecast if missing
            double lastForecastBalance = lastActualBalance;
            var lineValues = saturdays.Select(d =>
            {
                if (d < firstForecastSaturday && actualDict.ContainsKey(d))
                    return actualDict[d];
                if (d == firstForecastSaturday)
                    return lastActualBalance;
                if (d > firstForecastSaturday)
                {
                    if (forecastDict.ContainsKey(d))
                        lastForecastBalance = forecastDict[d];
                    return lastForecastBalance;
                }
                return double.NaN;
            }).ToArray();

            // Forecast dots: show a dot for every future Saturday, using last known forecast if missing
            lastForecastBalance = lastActualBalance;
            var forecastDotValues = saturdays.Select(d =>
            {
                if (d > DateTime.Today)
                {
                    if (forecastDict.ContainsKey(d))
                        lastForecastBalance = forecastDict[d];
                    return lastForecastBalance;
                }
                return double.NaN;
            }).ToArray();

            var allLabels = saturdays.Select(d => d.ToString("MM/dd/yyyy")).ToArray();

            if (account == "BMO Check")
            {
                BalanceChart.Series = new ISeries[]
                {
                    new LineSeries<double>
                    {
                        Values = lineValues,
                        Name = "Balance",
                        Fill = null,
                        Stroke = new SolidColorPaint(SKColors.SteelBlue, 3),
                        GeometrySize = 10,
                        LineSmoothness = 0.5
                    },
                    new LineSeries<double>
                    {
                        Values = forecastDotValues,
                        Name = "Forecasted Dots",
                        Fill = null,
                        Stroke = null,
                        GeometrySize = 14,
                        GeometryFill = new SolidColorPaint(SKColors.Orange),
                        GeometryStroke = new SolidColorPaint(SKColors.DarkOrange, 2)
                    }
                };

                BalanceChart.XAxes = new Axis[]
                {
                    new Axis
                    {
                        Name = "Week Ending (Saturday)",
                        Labels = allLabels,
                        LabelsRotation = 45,
                        MinStep = 1
                    }
                };
            }
            else
            {
                // Only show actuals up to the last Saturday with data
                var saturdaysWithActuals = saturdays
                    .Where(d => d <= DateTime.Today && actualDict.ContainsKey(d))
                    .ToList();

                var actualValues = saturdaysWithActuals
                    .Select(d => actualDict[d])
                    .ToArray();

                var actualLabels = saturdaysWithActuals
                    .Select(d => d.ToString("MM/dd/yyyy"))
                    .ToArray();

                BalanceChart.Series = new ISeries[]
                {
                    new LineSeries<double>
                    {
                        Values = actualValues,
                        Name = "Balance",
                        Fill = null,
                        Stroke = new SolidColorPaint(SKColors.SteelBlue, 3),
                        GeometrySize = 10,
                        LineSmoothness = 0.5
                    }
                };

                BalanceChart.XAxes = new Axis[]
                {
                    new Axis
                    {
                        Name = "Week Ending (Saturday)",
                        Labels = actualLabels,
                        LabelsRotation = 45,
                        MinStep = 1
                    }
                };
            }

            BalanceChart.YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Balance"
                }
            };
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}