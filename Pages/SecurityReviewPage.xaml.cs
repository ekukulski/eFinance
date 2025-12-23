using Microsoft.Maui.Controls;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KukiFinance.Pages
{
    public partial class SecurityReviewPage : ContentPage
    {
        private List<(string Security, decimal Amount)> _securities;
        private int _currentIndex;
        private bool _addingNew;
        public decimal RidaAmount { get; private set; }
        public string RidaDescription { get; private set; }
        public List<(string Security, decimal Amount)> FinalSecurities { get; private set; }
        public TaskCompletionSource<bool> ReviewCompleted { get; } = new();

        public SecurityReviewPage(List<(string Security, decimal Amount)> securities)
        {
            InitializeComponent();
            _securities = new List<(string Security, decimal Amount)>(securities);
            _currentIndex = 0;
            FinalSecurities = null;
            RidaAmount = 0;
            ShowCurrentSecurity();
        }

        private void ShowCurrentSecurity()
        {
            RidaPanel.IsVisible = false;

            // Always show the labels unless adding a new security
            SecurityNameLabel.IsVisible = true;
            SecurityAmountLabel.IsVisible = true;

            if (_securities.Count == 0 || _currentIndex < 0)
            {
                SecurityNameLabel.Text = "No securities. Please add one.";
                SecurityAmountLabel.Text = "";
                SecurityAmountLabel.IsVisible = false;
                AmountEntry.Text = "";
                AmountEntry.IsEnabled = true;
                AmountEntry.IsVisible = true;
                _addingNew = true;
                return;
            }

            if (_currentIndex < _securities.Count)
            {
                var sec = _securities[_currentIndex];
                SecurityNameLabel.Text = $"Security: {sec.Security}";
                SecurityAmountLabel.Text = $"Current Amount: {sec.Amount:C}";
                SecurityAmountLabel.IsVisible = true;
                AmountEntry.Text = sec.Amount.ToString();
                AmountEntry.IsEnabled = false;
                AmountEntry.IsVisible = false;
                _addingNew = false;
            }
            else
            {
                SecurityNameLabel.Text = "All securities reviewed.";
                SecurityAmountLabel.Text = "";
                SecurityAmountLabel.IsVisible = false;
                AmountEntry.Text = "";
                AmountEntry.IsEnabled = false;
                AmountEntry.IsVisible = false;
                _addingNew = false;
            }
        }

        private async void OnEditClicked(object sender, System.EventArgs e)
        {
            if (_currentIndex < _securities.Count)
            {
                // Prompt for new name
                string newName = await DisplayPromptAsync("Edit Security", "Enter new name:", initialValue: _securities[_currentIndex].Security);

                // Prompt for new amount
                string newAmtStr = await DisplayPromptAsync("Edit Amount", "Enter new amount:", initialValue: _securities[_currentIndex].Amount.ToString());

                // Validate and update
                if (!string.IsNullOrWhiteSpace(newName) && decimal.TryParse(newAmtStr, out var newAmt))
                {
                    _securities[_currentIndex] = (newName, newAmt);
                }
                else if (!string.IsNullOrWhiteSpace(newName))
                {
                    // Only name changed
                    _securities[_currentIndex] = (newName, _securities[_currentIndex].Amount);
                }
                else if (decimal.TryParse(newAmtStr, out newAmt))
                {
                    // Only amount changed
                    _securities[_currentIndex] = (_securities[_currentIndex].Security, newAmt);
                }

                AmountEntry.IsEnabled = false;
                AmountEntry.IsVisible = false;
                SecurityAmountLabel.IsVisible = true;
                ShowCurrentSecurity();
            }
        }

        private void OnAddNewClicked(object sender, System.EventArgs e)
        {
            SecurityNameLabel.Text = "Security: ";
            SecurityAmountLabel.Text = "";
            SecurityAmountLabel.IsVisible = false;
            AmountEntry.Text = "";
            AmountEntry.IsEnabled = true;
            AmountEntry.IsVisible = true;
            _addingNew = true;
        }

        private async void OnFinishClicked(object sender, EventArgs e)
        {
            if (decimal.TryParse(RidaEntry.Text, out var ridaAmt))
                RidaAmount = ridaAmt;
            else
                RidaAmount = 0;

            RidaDescription = RidaDescriptionEntry.Text?.Trim() ?? "";
            FinalSecurities = new List<(string Security, decimal Amount)>(_securities);

            ReviewCompleted.TrySetResult(true);
            await Navigation.PopModalAsync();
        }

        private void OnDeleteClicked(object sender, System.EventArgs e)
        {
            if (_currentIndex < _securities.Count)
            {
                _securities.RemoveAt(_currentIndex);
                if (_currentIndex >= _securities.Count)
                    _currentIndex = _securities.Count - 1;
                ShowCurrentSecurity();
            }
        }

        private async void OnNextClicked(object sender, System.EventArgs e)
        {
            if (_addingNew)
            {
                // Prompt for new security name
                string name = await DisplayPromptAsync("Add Security", "Enter security name:");
                // Prompt for new amount
                string amtStr = await DisplayPromptAsync("Add Security", "Enter amount:", keyboard: Keyboard.Numeric);

                if (!string.IsNullOrWhiteSpace(name) && decimal.TryParse(amtStr, out var amt))
                {
                    _securities.Add((name, amt));
                    _addingNew = false;
                    _currentIndex = _securities.Count - 1; // Show the newly added security
                    ShowCurrentSecurity();
                }
                else
                {
                    await DisplayAlert("Invalid", "Please enter a valid name and amount.", "OK");
                }
            }
            else
            {
                _currentIndex++;
                if (_currentIndex >= _securities.Count)
                {
                    PromptAddNewOrRida();
                }
                else
                {
                    ShowCurrentSecurity();
                }
            }
        }

        private async void PromptAddNewOrRida()
        {
            bool addNew = await DisplayAlert("Add Security", "Add a new security?", "Yes", "No");
            if (addNew)
            {
                // Prompt for security name
                string name = await DisplayPromptAsync("Add Security", "Enter security name:");
                if (string.IsNullOrWhiteSpace(name))
                {
                    await DisplayAlert("Invalid", "Please enter a valid security name.", "OK");
                    return;
                }

                // Prompt for amount
                string amtStr = await DisplayPromptAsync("Add Security", "Enter amount:", keyboard: Keyboard.Numeric);
                if (!decimal.TryParse(amtStr, out var amt))
                {
                    await DisplayAlert("Invalid", "Please enter a valid amount.", "OK");
                    return;
                }

                // Add new security and show it
                _securities.Add((name, amt));
                _addingNew = false;
                _currentIndex = _securities.Count - 1; // Show the newly added security
                ShowCurrentSecurity();
            }
            else
            {
                // Show RIDA panel
                RidaPanel.IsVisible = true;
                SecurityNameLabel.Text = "Enter RIDA amount below.";
                SecurityAmountLabel.Text = "";
                SecurityAmountLabel.IsVisible = false;
            }
        }
    }
}