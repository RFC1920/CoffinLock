#region License (GPL v3)
/*
    DESCRIPTION
    Copyright (c) 2020 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v3)
#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Coffin Lock", "RFC1920", "1.0.3")]
    [Description("Lock coffins and dropboxes with a code lock")]
    class CoffinLock : RustPlugin
    {
        #region vars
        const string codeLockPrefab = "assets/prefabs/locks/keypad/lock.code.prefab";
        public Quaternion entityrot;
        public Vector3 entitypos;
        public BaseEntity newlock;

        public Dictionary<ulong,bool> userenabled = new Dictionary<ulong,bool>();
        public Dictionary<int,coffinpair> coffinpairs = new Dictionary<int,coffinpair>();
        public List<ulong> coffins = new List<ulong>();
        private const string permCoffinlockUse = "coffinlock.use";
        private const string permCoffinlockAdmin = "coffinlock.admin";

        public class coffinpair
        {
            public ulong owner;
            public ulong coffinid;
            public ulong lockid;
        }
        private bool g_configChanged;
        private bool ownerBypass = false;
        private bool adminBypass = false;
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        #region init
        void Init()
        {
            AddCovalenceCommand("cl", "cmdCoffinLock");

            permission.RegisterPermission(permCoffinlockUse, this);
            permission.RegisterPermission(permCoffinlockAdmin, this);

            LoadData();
        }

        void Loaded() => LoadConfigValues();

        protected override void LoadDefaultConfig() => Puts("New configuration file created.");

        void LoadConfigValues()
        {
            ownerBypass = Convert.ToBoolean(GetConfigValue("Settings", "Owner can bypass lock", false));
            adminBypass = Convert.ToBoolean(GetConfigValue("Settings", "Admin can bypass lock", false));

            if(g_configChanged)
            {
                Puts("Configuration file updated.");
                SaveConfig();
            }
        }

        object GetConfigValue(string category, string setting, object defaultValue)
        {
            Dictionary<string, object> data = Config[category] as Dictionary<string, object>;
            object value;

            if(data == null)
            {
                data = new Dictionary<string, object>();
                Config[category] = data;
                g_configChanged = true;
            }

            if(data.TryGetValue(setting, out value)) return value;
            value = defaultValue;
            data[setting] = value;
            g_configChanged = true;
            return value;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cannotdo"] = "You cannot remove a lock which is part of a coffin.",
                ["notauthorized"] = "You don't have permission to use this command.",
                ["instructions"] = "/cl on to enable, /cl off to disable.",
                ["spawned"] = "CoffinLock spawned a new lockable coffin!",
                ["dspawned"] = "CoffinLock spawned a new lockable dropbox!",
                ["failed"] = "CoffinLock failed to spawn a new lockable coffin!",
                ["dfailed"] = "CoffinLock failed to spawn a new lockable dropbox!",
                ["locked"] = "This coffin is locked!",
                ["dlocked"] = "This dropbox is locked!",
                ["unlocked"] = "This coffin is unlocked!",
                ["dunlocked"] = "This dropbox is unlocked!",
                ["disabled"] = "CoffinLock is disabled.",
                ["enabled"] = "CoffinLock is enabled.",
                ["owner"] = "Coffin owned by {0}.",
                ["nocoffin"] = "Could not find a coffin in front of you.",
                ["nodropbox"] = "Could not find a dropbox in front of you."
            }, this);
        }

        void Unload()
        {
            SaveData();
        }

        private void LoadData()
        {
            userenabled = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, bool>>(Name + "/coffinlock_user");
            coffinpairs = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<int, coffinpair>>(Name + "/coffinlock_data");
            foreach(KeyValuePair<int, coffinpair> coffindata in coffinpairs)
            {
                coffins.Add(coffindata.Value.coffinid);
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/coffinlock_user", userenabled);
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/coffinlock_data", coffinpairs);
        }
        #endregion

        #region Rust_Hooks
        private void OnEntitySpawned(BaseEntity entity)
        {
            if(entity == null) return;
            if(entity.name.Contains("coffin"))
            {
                BasePlayer player = FindOwner(entity.OwnerID);
                if(player == null)
                {
#if DEBUG
                    Puts($"Could not find owner of this coffin.");
#endif
                    return;
                }

                if(!player.IPlayer.HasPermission(permCoffinlockUse))
                {
#if DEBUG
                    Puts($"Player {player.displayName} denied permission.");
#endif
                    return;
                }
                if(!userenabled.ContainsKey(player.userID))
                {
#if DEBUG
                    Puts($"Player {player.displayName} has never enabled CoffinLock.");
#endif
                    Message(player.IPlayer, "disabled");
                    return;
                }
                if(userenabled[player.userID] == false)
                {
#if DEBUG
                    Puts($"Player {player.displayName} has CoffinLock disabled.");
#endif
                    Message(player.IPlayer, "disabled");
                    return;
                }

                if(AddLock(entity))
                {
                    coffins.Add(entity.net.ID);
                    Message(player.IPlayer, "spawned");
                    SaveData();
#if DEBUG
                    Puts($"Spawned coffin with lock");
#endif
                }
                else
                {
#if DEBUG
                    Puts($"Failed to spawn coffin with lock");
#endif
                    Message(player.IPlayer, "failed");
                }
                player = null;
            }
            else if(entity.name.Contains("dropbox"))
            {
                BasePlayer player = FindOwner(entity.OwnerID);
                if(player == null)
                {
#if DEBUG
                    Puts($"Could not find owner of this dropbox.");
#endif
                    return;
                }

                if(!player.IPlayer.HasPermission(permCoffinlockUse))
                {
#if DEBUG
                    Puts($"Player {player.displayName} denied permission.");
#endif
                    return;
                }
                if(!userenabled.ContainsKey(player.userID))
                {
#if DEBUG
                    Puts($"Player {player.displayName} has never enabled CoffinLock.");
#endif
                    Message(player.IPlayer, "disabled");
                    return;
                }
                if(userenabled[player.userID] == false)
                {
#if DEBUG
                    Puts($"Player {player.displayName} has CoffinLock disabled.");
#endif
                    Message(player.IPlayer, "disabled");
                    return;
                }

                if(AddLock(entity, true))
                {
                    coffins.Add(entity.net.ID);
                    Message(player.IPlayer, "dspawned");
                    SaveData();
#if DEBUG
                    Puts($"Spawned dropbox with lock");
#endif
                }
                else
                {
#if DEBUG
                    Puts($"Failed to spawn dropbox with lock");
#endif
                    Message(player.IPlayer, "dfailed");
                }
                player = null;
            }
        }

        private BaseEntity CheckParent(BaseEntity entity)
        {
            if(entity.HasParent())
            {
                BaseEntity parententity = entity.GetParentEntity();
                if(parententity is MiningQuarry)
                {
                    entity.OwnerID=parententity.OwnerID;
                    entity=parententity;
                }
            }
            return entity;
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            BaseEntity entity = container as BaseEntity;
            if (entity == null) return null;
            BaseEntity myent = CheckParent(entity);
            if (myent == null) return null;

            if(IsOurcoffin(myent.net.ID))
            {
                if(myent.name.Contains("coffin"))
                {
                    if(IsLocked(myent.net.ID))
                    {
#if DEBUG
                        Puts("CanLootEntity: Player trying to open our locked coffin!");
#endif
                        if(myent.OwnerID == player.userID && ownerBypass)
                        {
#if DEBUG
                            Puts("CanLootEntity: Per config, owner can bypass");
#endif
                            return null;
                        }
                        if(player.IPlayer.HasPermission(permCoffinlockAdmin) && adminBypass)
                        {
#if DEBUG
                            Puts("CanLootEntity: Per config, admin can bypass.");
#endif
                            return null;
                        }

                        Message(player.IPlayer, "locked");
                        return true;
                    }
                    else
                    {
#if DEBUG
                        Puts("CanLootEntity: Player opening our unlocked coffin!");
#endif
                        return null;
                    }
                }
                else if(myent.name.Contains("dropbox"))
                {
                    if(IsLocked(myent.net.ID))
                    {
#if DEBUG
                        Puts("CanLootEntity: Player trying to open our locked dropbox!");
#endif
                        if(myent.OwnerID == player.userID && ownerBypass)
                        {
#if DEBUG
                            Puts("CanLootEntity: Per config, owner can bypass");
#endif
                            return null;
                        }
                        if(player.IPlayer.HasPermission(permCoffinlockAdmin) && adminBypass)
                        {
#if DEBUG
                            Puts("CanLootEntity: Per config, admin can bypass.");
#endif
                            return null;
                        }

                        Message(player.IPlayer, "dlocked");
                        return true;
                    }
                    else
                    {
#if DEBUG
                        Puts("CanLootEntity: Player opening our unlocked dropbox!");
#endif
                        return null;
                    }
                }
            }
            return null;
        }

        // Check for our coffin
        private object CanPickupEntity(BasePlayer player, BaseEntity myent)
        {
            if(myent == null) return null;
            if(player == null) return null;

            if(IsOurcoffin(myent.net.ID))
            {
                if(myent.name.Contains("coffin"))
                {
                    if(IsLocked(myent.net.ID))
                    {
#if DEBUG
                        Puts("CanPickupEntity: Player trying to pickup our locked coffin!");
#endif
                        Message(player.IPlayer, "locked");
                        return false;
                    }
                    else
                    {
#if DEBUG
                        Puts("CanPickupEntity: Player picking up our unlocked coffin!");
#endif
                        coffins.Remove(myent.net.ID);
                        int mycoffin = coffinpairs.FirstOrDefault(x => x.Value.coffinid == myent.net.ID).Key;
                        coffinpairs.Remove(mycoffin);
                        SaveData();
                        return null;
                    }
                }
                else if(myent.name.Contains("dropbox"))
                {
                    if(IsLocked(myent.net.ID))
                    {
#if DEBUG
                        Puts("CanPickupEntity: Player trying to pickup our locked dropbox!");
#endif
                        Message(player.IPlayer, "dlocked");
                        return false;
                    }
                    else
                    {
#if DEBUG
                        Puts("CanPickupEntity: Player picking up our unlocked dropbox!");
#endif
                        coffins.Remove(myent.net.ID);
                        int mycoffin = coffinpairs.FirstOrDefault(x => x.Value.coffinid == myent.net.ID).Key;
                        coffinpairs.Remove(mycoffin);
                        SaveData();
                        return null;
                    }
                }
            }
            return null;
        }

        // Check for our coffin lock, block pickup
        private object CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if(baseLock == null) return null;
            if(player == null) return null;

            BaseEntity ecoffin = baseLock.GetParentEntity();
            if(ecoffin == null) return null;

            if((ecoffin.name.Contains("coffin") || ecoffin.name.Contains("dropbox")) && IsOurcoffin(ecoffin.net.ID))
            {
#if DEBUG
                Puts("CanPickupLock: Player trying to remove lock from a locked coffin/dropbox!");
#endif
                Message(player.IPlayer, "cannotdo");
                return false;
            }
            return null;
        }

        void OnNewSave(string strFilename)
        {
            // Wipe the dict of coffin pairs.  But, player prefs are maintained.
            coffinpairs = new Dictionary<int,coffinpair>();
            SaveData();
        }
        #endregion

        #region Main
        [Command("cl")]
        void cmdCoffinLock(IPlayer player, string command, string[] args)
        {
            if(!player.HasPermission(permCoffinlockUse)) { Message(player, "notauthorized"); return; }
            ulong playerID = ulong.Parse(player.Id);

            if(args.Length == 0)
            {
                if(!userenabled.ContainsKey(playerID))
                {
                    Message(player, "disabled");
                    Message(player, "instructions");
                }
                else if(userenabled[playerID] == false)
                {
                    Message(player, "disabled");
                    Message(player, "instructions");
                }
                else if(userenabled[playerID] == true)
                {
                    Message(player, "enabled");
                    Message(player, "instructions");
                }
                return;
            }
            if(args[0] == "on" || args[0] == "1")
            {
                if(!userenabled.ContainsKey(playerID))
                {
                    userenabled.Add(playerID, true);
                }
                else if(userenabled[playerID] == false)
                {
                    userenabled[playerID] = true;
                }
                Message(player, "enabled");
            }
            else if(args[0] == "off" || args[0] == "0")
            {
                if(!userenabled.ContainsKey(playerID))
                {
                    userenabled.Add(playerID, false);
                }
                else if(userenabled[playerID] == true)
                {
                    userenabled[playerID] = false;
                }
                Message(player, "disabled");
            }
            else if(args[0] == "who" && player.HasPermission(permCoffinlockAdmin))
            {
                RaycastHit hit;
                BasePlayer basePlayer = player.Object as BasePlayer;
                if(Physics.Raycast(basePlayer.eyes.HeadRay(), out hit, 2.2f))
                {
                    BaseEntity ecoffin = hit.GetEntity();
                    BasePlayer owner = FindOwner(ecoffin.OwnerID);
                    Message(player, "owner", owner.displayName);
                    ecoffin = null;
                    owner = null;
                }
                else
                {
                    Message(player, "nocoffin");
                }
            }
            SaveData();
        }

        // Lock entity spawner
        private bool AddLock(BaseEntity ecoffin, bool dropbox = false)
        {
            newlock = new BaseEntity();
            if(newlock = GameManager.server.CreateEntity(codeLockPrefab, entitypos, entityrot, true))
            {
                if(dropbox)
                {
                    newlock.transform.localEulerAngles = new Vector3(0, 90, 0);
                    newlock.transform.localPosition = new Vector3(0.15f, 0f, -0.4f);
                }
                else
                {
                    newlock.transform.localEulerAngles = new Vector3(0, 96, 0);
                    newlock.transform.localPosition = new Vector3(-0.15f, 0.35f, 0.4f);
                }

                newlock.SetParent(ecoffin, 0);
                newlock?.Spawn();
                newlock.OwnerID = ecoffin.OwnerID;

                int id = UnityEngine.Random.Range(1, 99999999);
                coffinpairs.Add(id, new coffinpair
                {
                    owner = ecoffin.OwnerID,
                    coffinid = ecoffin.net.ID,
                    lockid = newlock.net.ID
                });
                return true;
            }
            return false;
        }

        // Used to find the owner of a coffin
        private BasePlayer FindOwner(ulong playerID)
        {
            if(playerID == 0) return null;
            IPlayer iplayer = covalence.Players.FindPlayer(playerID.ToString());

            if(iplayer != null)
            {
                return iplayer.Object as BasePlayer;
            }
            else
            {
                return null;
            }
        }

        private bool IsOurcoffin(ulong coffinid)
        {
            if(coffinid == 0) return false;
            foreach(KeyValuePair<int, coffinpair> coffindata in coffinpairs)
            {
                if(coffindata.Value.coffinid == coffinid)
                {
#if DEBUG
                    Puts("This is one of our coffins!");
#endif
                    return true;
                }
            }
            return false;
        }

        // Check whether this coffin has an associated lock, and whether or not it is locked
        private bool IsLocked(ulong coffinid)
        {
            if(coffinid == 0) return false;
            foreach(KeyValuePair<int, coffinpair> coffindata in coffinpairs)
            {
                if(coffindata.Value.coffinid == coffinid)
                {
                    var mylockid =  Convert.ToUInt32(coffindata.Value.lockid);
                    var bent = BaseNetworkable.serverEntities.Find(mylockid);
                    var lockent = bent as BaseEntity;
                    if(lockent.IsLocked())
                    {
#if DEBUG
                        Puts("Found an associated lock!");
#endif
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion
    }
}
