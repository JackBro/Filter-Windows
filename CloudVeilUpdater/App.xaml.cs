﻿using CloudVeilInstallerUI;
using CloudVeilInstallerUI.IPC;
using CloudVeilInstallerUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CloudVeilUpdater
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            UpdateIPCClient client = new UpdateIPCClient("__CloudVeilUpdaterPipe__");
            Thread.CurrentThread.Name = "MainThread";

            Console.WriteLine("OnStartup Thread Id = {0}", Thread.CurrentThread.ManagedThreadId);

            RemoteInstallerViewModel model = new RemoteInstallerViewModel(client);
            ISetupUI setupUi = null;

            setupUi = new MainWindow(model, true);

            client.RegisterObject("SetupUI", setupUi);
            client.RegisterObject("InstallerViewModel", model);
            client.Start();

            Console.WriteLine("Client Waiting for connection");
            client.WaitForConnection();
            Console.WriteLine("Client connected");

            setupUi.Closed += (sender, _e) =>
            {
                client.PushMessage(new Message()
                {
                    Command = Command.Exit
                });
            };

            setupUi.Show();

            base.OnStartup(e);
        }
    }
}
