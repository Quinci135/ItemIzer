using System;
using System.Collections.Generic;
using System.Linq;
using TShockAPI;
using TShockAPI.DB;
using System.IO;
using MySql.Data.MySqlClient;
using Mono.Data.Sqlite;
using Terraria;
using TerrariaApi.Server;
using System.Threading.Tasks;
using System.Data;

namespace ItemIzer
{
    [ApiVersion(2, 1)]
    public class ItemIzer : TerrariaPlugin
    {
        public override string Author => "Quinci";

        public override string Description => "Managing items in ssc";

        public override string Name => "ItemIzer";

        public override Version Version => new Version(1, 0, 0, 0);

        private static IDbConnection database;

        public int Here;

        public ItemIzer(Main game) : base(game) 
        {
            Order = 30;
        }

        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInit);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInit);
            }
            base.Dispose(disposing);
        }

        private void OnInit(EventArgs args)
        {
            if (Main.ServerSideCharacter == false)
            {
                TShock.Log.Warn("[ItemIzer] Server side characters is not enabled! Disabling.");
                Dispose(true);
                return;
            }
            if (TShock.Config.StorageType.ToLower() == "mysql")
            {
                string[] host = TShock.Config.MySqlHost.Split(':');
                database = new MySqlConnection()
                {
                    ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                                    host[0],
                                    host.Length == 1 ? "3306" : host[1],
                                    TShock.Config.MySqlDbName,
                                    TShock.Config.MySqlUsername,
                                    TShock.Config.MySqlPassword)
                };
            }
            else if (TShock.Config.StorageType.ToLower() == "sqlite")
            {
                string sql = Path.Combine(TShock.SavePath, "tshock.sqlite");
                database = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
            }
            Commands.ChatCommands.AddRange(new List<Command>
            {
                new Command("itemizer.check", WhoHas, "whohas") { HelpText = "Checks who has the specified item." },
                new Command("itemizer.check", CheckPlayer, "checkplayer") { HelpText = "Checks if the given player has the specified item" },
                new Command("itemizer.edit", RemoveFromPlayer, "removefromplayer") { HelpText = "Removes the specified item from the given player" }
            });
        }

        private async void WhoHas(CommandArgs args)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (args.Parameters.Count < 1)
                    {
                        args.Player.SendErrorMessage($"Invalid Syntax! Valid Sytax: {TShock.Config.CommandSpecifier}whohas <Item name or ID>");
                        return;
                    }
                    
                    NetItem item = new NetItem();
                    try
                    {
                        List<Item> items = TShock.Utils.GetItemByIdOrName(args.Parameters[0]);
                        Item targetItem = null;
                        if (items.Count == 0)
                        {
                            throw new Exception();
                        }
                        if (items.Count > 1)
                        {
                            args.Player.SendErrorMessage($"{args.Parameters[0]} matched multiple items:");
                            args.Player.SendMultipleMatchError(items.Select(i => $"{i.Name}({i.netID})"));
                            return;
                        }
                        else
                        {
                            targetItem = TShock.Utils.GetItemByIdOrName(args.Parameters[0])[0];
                            item = NetItem.Parse($"{targetItem.netID}, 0, 0");
                        }
                    }
                    catch
                    {
                        args.Player.SendErrorMessage($"\"{args.Parameters[0]}\" was not a valid item.");
                        return;
                    }
                    Dictionary<string, int> result = new Dictionary<string, int>();
                    foreach (TSPlayer player in TShock.Players)
                    {
                        if (player != null && player.IsLoggedIn && !player.IsDisabledPendingTrashRemoval)
                        {
                            TShock.CharacterDB.InsertPlayerData(player);
                        }
                    }
                    Task.Delay(600);
                    using (QueryResult reader = database.QueryReader("SELECT * FROM tsCharacter"))
                    {
                        while (reader.Read())
                        {
                            List<NetItem> inventoryList = reader.Get<string>("Inventory").Split('~').Select(NetItem.Parse).ToList();
                            int stack = 0;
                            inventoryList.ForEach(i =>
                            {
                                if (i.NetId == item.NetId)
                                {
                                    stack += i.Stack;
                                }
                            });
                            if (stack > 0)
                            {
                                result.Add(TShock.UserAccounts.GetUserAccountByID(reader.Get<int>("Account")).Name, stack);
                            }
                        }
                    }
                    if (result.Count == 0)
                    {
                        args.Player.SendErrorMessage($"Nobody was found with item \"{TShock.Utils.GetItemById(item.NetId).Name}\"");
                    }
                    else if (result.Count == 1)
                    {
                        args.Player.SendInfoMessage($"{result.First().Key} was found with item \"{TShock.Utils.GetItemById(item.NetId).Name}\"(id: {item.NetId}, total: {result.First().Value})");
                    }
                    else
                    {
                        args.Player.SendInfoMessage($"Item \"{TShock.Utils.GetItemById(item.NetId).Name}\" was found in players:");
                        string foundPlayers = "";
                        result.ForEach((i) =>
                        {
                            foundPlayers += $"\"{i.Key}\" (total: {i.Value}), ";
                        });
                        args.Player.SendInfoMessage(foundPlayers.Remove(foundPlayers.Length - 2) + ".");
                    }
                }
                catch (Exception e)
                {
                    TShock.Log.Warn($"Plugin [ItemIzer] threw an exception:!\n{e}");
                }
            });
        }

        private async void CheckPlayer(CommandArgs args)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (args.Parameters.Count == 2)
                    {
                        UserAccount account = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
                        if (account == null)
                        {
                            args.Player.SendErrorMessage($"Player/Account \"{args.Parameters[1]}\" could not be found.");
                        }
                        else
                        {
                            foreach (TSPlayer plr in TShock.Players)
                            {
                                if (plr != null && plr.IsLoggedIn && !plr.IsDisabledPendingTrashRemoval && plr.Account == account)
                                {
                                    TShock.CharacterDB.InsertPlayerData(plr);
                                }
                            }
                            NetItem item = new NetItem();
                            try
                            {
                                List<Item> items = TShock.Utils.GetItemByIdOrName(args.Parameters[1]);
                                Item targetItem = null;
                                if (items.Count == 0)
                                {
                                    throw new Exception();
                                }
                                if (items.Count > 1)
                                {
                                    args.Player.SendErrorMessage($"{args.Parameters[1]} matched multiple items:");
                                    args.Player.SendMultipleMatchError(items.Select(i => $"{i.Name}({i.netID})"));
                                    return;
                                }
                                else
                                {
                                    targetItem = TShock.Utils.GetItemByIdOrName(args.Parameters[1])[0];
                                    item = NetItem.Parse($"{targetItem.netID}, 0, 0");
                                }
                            }
                            catch
                            {
                                args.Player.SendErrorMessage($"\"{args.Parameters[1]}\" was not a valid item.");
                                return;
                            }
                            int stack = 0;
                            Task.Delay(600);
                            using (QueryResult reader = database.QueryReader("SELECT * FROM tsCharacter WHERE Account =@0", account.ID))
                            {
                                if (reader.Read())
                                {
                                    List<NetItem> inventoryList = reader.Get<string>("Inventory").Split('~').Select(NetItem.Parse).ToList();
                                    inventoryList.ForEach(i =>
                                    {
                                        if (i.NetId == item.NetId)
                                        {
                                            stack += i.Stack;
                                        }
                                    });
                                }
                            }
                            if (stack > 0)
                            {
                                args.Player.SendInfoMessage($"Item \"{TShock.Utils.GetItemById(item.NetId).Name}\" (total: {stack}) was found in {account.Name}'s inventory");
                            }
                            else
                            {
                                args.Player.SendErrorMessage($"{account.Name} was not found with item \"{TShock.Utils.GetItemById(item.NetId).Name}\"(id: {item.NetId})");
                            }
                        }
                    }
                    else
                    {
                        args.Player.SendErrorMessage($"Invalid Syntax! Valid Sytax: {TShock.Config.CommandSpecifier}checkplayer <Exact Player Name> <Item Name or ID>");
                    }
                }
                catch (Exception e)
                {
                    TShock.Log.Warn($"Plugin [ItemIzer] threw an exception:!\n{e}");
                }
            });
        }

        private async void RemoveFromPlayer(CommandArgs args)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (args.Parameters.Count == 2)
                    {
                        UserAccount account = TShock.UserAccounts.GetUserAccountByName(args.Parameters[0]);
                        if (account == null)
                        {
                            args.Player.SendErrorMessage($"Player/Account \"{args.Parameters[1]}\" could not be found.");
                        }
                        else
                        {
                            TSPlayer player = null;
                            foreach (TSPlayer plr in TShock.Players)
                            {
                                if (plr != null && plr.IsLoggedIn && !plr.IsDisabledPendingTrashRemoval && plr.Account == account)
                                {
                                    player = plr;
                                    TShock.CharacterDB.InsertPlayerData(plr);
                                }
                            }
                            NetItem item = new NetItem();
                            try
                            {
                                List<Item> items = TShock.Utils.GetItemByIdOrName(args.Parameters[1]);
                                Item targetItem = null;
                                if (items.Count == 0)
                                {
                                    throw new Exception();
                                }
                                if (items.Count > 1)
                                {
                                    args.Player.SendErrorMessage($"{args.Parameters[1]} matched multiple items:");
                                    args.Player.SendMultipleMatchError(items.Select(i => $"{i.Name}({i.netID})"));
                                    return;
                                }
                                else
                                {
                                    targetItem = TShock.Utils.GetItemByIdOrName(args.Parameters[1])[0];
                                    item = NetItem.Parse($"{targetItem.netID}, 0, 0");
                                }

                            }
                            catch
                            {
                                args.Player.SendErrorMessage($"\"{args.Parameters[1]}\" was not a valid item.");
                                return;
                            }

                            int stack = 0;
                            Task.Delay(600);
                            List<NetItem> replaceList = new List<NetItem> { };

                            using (QueryResult reader = database.QueryReader("SELECT * FROM tsCharacter WHERE Account =@0", account.ID))
                            {
                                if (reader.Read())
                                {
                                    List<NetItem> inventoryList = reader.Get<string>("Inventory").Split('~').Select(NetItem.Parse).ToList();
                                    inventoryList.ForEach(i =>
                                    {
                                        if (i.NetId == item.NetId)
                                        {
                                            stack += i.Stack;
                                            replaceList.Add(new NetItem(0, 0, 0));
                                        }
                                        else
                                        {
                                            replaceList.Add(i);
                                        }
                                    });
                                }
                            }

                            PlayerData playerData = new PlayerData(new TSPlayer(-1));
                            playerData.inventory = replaceList.ToArray();
                            database.Query("UPDATE tsCharacter SET Inventory = @0 WHERE Account = @1", string.Join("~", playerData.inventory), account.ID);
                            if (stack > 0)
                            {
                                if (player != null)
                                {
                                    player.PlayerData.inventory = playerData.inventory;
                                    player.PlayerData.RestoreCharacter(player); 
                                }
                                args.Player.SendInfoMessage($"Removed {stack} items of type \"{TShock.Utils.GetItemById(item.NetId).Name}\" from {account.Name}'s inventory");
                            }
                            else
                            {
                                args.Player.SendErrorMessage($"{account.Name} was not found with item \"{TShock.Utils.GetItemById(item.NetId).Name}\"(id: {item.NetId})");
                            }
                        }
                    }
                    else
                    {
                        args.Player.SendErrorMessage($"Invalid Syntax! Valid Sytax: {TShock.Config.CommandSpecifier}removefromplayer <Exact Player Name> <Item Name or ID>");
                    }
                }
                catch (Exception e)
                {
                    TShock.Log.Warn($"Plugin [ItemIzer] threw an exception:!\n{e}");
                }
            });
        }
    }
}
