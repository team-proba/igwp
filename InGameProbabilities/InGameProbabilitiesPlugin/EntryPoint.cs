﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using InGameProbabilitiesPlugin.GameData;
using InGameProbabilitiesPlugin.Network;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InGameProbabilitiesPlugin.InjectionManager;

namespace InGameProbabilitiesPlugin
{
    public class EntryPoint
    {
        private const string InjectionDll = @"LeagueReplayHook.dll";
        
        private readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        
        private Task listeningTask;

        private double winChance;

        private readonly NetworkInterface networkInterface;

        private StateManager stateManager;

        public EntryPoint()
        {
            networkInterface = new NetworkInterface("http://54.183.147.234", 3000, "RGAPI-e4491f0b-b99a-49c4-b817-5f9b00267da1");
        }

        public void StartApp(Action<object> callback)
        {
            Task.Run(() =>
            {
                var listener = new GameEventListener(7000);
                var transpiler = new MessageTranspiler();
                var injector = new LeagueInjectionManager();
                
                var path = $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}\\{InjectionDll}";
                if (!injector.Inject(path))
                {
                    Console.Error.WriteLine("league appears to not be running (or injection failed)!");
                }
                
                this.listeningTask = Task.Run(
                    () =>
                    {
                        try
                        {
                            var count = 0;
                            while (!this.tokenSource.IsCancellationRequested)
                            {
                                count++;
                                var rawMessage = listener.GetMessage();
                                var messages = transpiler.Translate(rawMessage);
                                foreach (var message in messages)
                                {
                                    stateManager.UpdateState(message);
                                }

                                if (count > 50)
                                {
                                    var result = networkInterface.Post("/getmodel", stateManager.GetCurrentState());
                                    count = 0;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }

                    },
                    this.tokenSource.Token);

                callback?.Invoke(true);
            });

        }

        public void InitializeState(string[] championNames, string[] blueTeam, string[] redTeam, Action<object> callback)
        {
            Task.Run(() =>
            {
                var summonerIdsBlue = networkInterface.GetSummonerIds(blueTeam);
                var summonerIdsRed = networkInterface.GetSummonerIds(redTeam);
                var leaguesBlue = networkInterface.GetRank(summonerIdsBlue);
                var leaguesRed = networkInterface.GetRank(summonerIdsRed);
                var championIds = networkInterface.GetChampionIds(championNames);

                stateManager = new StateManager(championIds, leaguesBlue, leaguesRed);

                callback?.Invoke(true);
            });
        }

        public double BlueWinChance => winChance;

        public double RedWinChange => 1 - winChance;

        public void printMessages(GameMessage[] messages)
        {
            foreach (var message in messages)
            {
                var teamId = Enum.GetName(typeof(TeamID), message.teamId);
                var type = Enum.GetName(typeof(MessageType), message.type);
                Console.WriteLine("Team: {0}, Type: {1}, Value: {2}", teamId, type, message.value);
            }
        }
    }
}
