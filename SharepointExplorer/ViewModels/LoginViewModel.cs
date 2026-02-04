using System;
using System.Reactive;
using System.Security;
using ReactiveUI;
using SharepointExplorer.Services;

namespace SharepointExplorer.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private string _siteUrl = "https://yourtenant.sharepoint.com/sites/yoursite";
        public string SiteUrl
        {
            get => _siteUrl;
            set => this.RaiseAndSetIfChanged(ref _siteUrl, value);
        }

        private string _username = "";
        public string Username
        {
            get => _username;
            set => this.RaiseAndSetIfChanged(ref _username, value);
        }

        private string _password = "";
        public string Password
        {
            get => _password;
            set => this.RaiseAndSetIfChanged(ref _password, value);
        }

        private string _clientId = "31359c7f-bd7e-475c-86db-fdb8c937548e";
        public string ClientId
        {
            get => _clientId;
            set => this.RaiseAndSetIfChanged(ref _clientId, value);
        }

        private string _redirectUri = "http://localhost";
        public string RedirectUri
        {
            get => _redirectUri;
            set => this.RaiseAndSetIfChanged(ref _redirectUri, value);
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public ReactiveCommand<Unit, bool> ConnectCommand { get; }
        public ReactiveCommand<Unit, bool> ModernConnectCommand { get; }

        public event Action? LoginSuccess;

        public LoginViewModel()
        {
            ConnectCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                StatusMessage = "Connecting with credentials...";
                var securePass = new SecureString();
                foreach (char c in Password) securePass.AppendChar(c);
                
                bool success = await AppServices.SharePoint.ConnectAsync(SiteUrl, Username, securePass);
                
                if (success)
                {
                    StatusMessage = "Connected!";
                    LoginSuccess?.Invoke();
                    return true;
                }
                else
                {
                    StatusMessage = "Connection Failed. Check credentials.";
                    return false;
                }
            });

            ModernConnectCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                StatusMessage = "Connecting interactively... Please check for browser popup.";
                bool success = await AppServices.SharePoint.ConnectInteractiveAsync(SiteUrl, ClientId, RedirectUri);
                
                if (success)
                {
                    StatusMessage = "Connected!";
                    LoginSuccess?.Invoke();
                    return true;
                }
                else
                {
                    StatusMessage = "Interactive Connection Failed. If using custom App ID, ensure it is consented.";
                    return false;
                }
            });
        }
    }
}