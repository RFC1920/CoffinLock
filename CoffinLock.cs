#region License (GPL v2)
/*
    CoffinLock - Lock coffins and dropboxes with a code lock
    Copyright (c) 2022 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License v2.0

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v2)
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Coffin Lock", "RFC1920", "1.0.6")]
    [Description("Lock coffins and dropboxes with a code lock")]
    internal class CoffinLock : RustPlugin
    {
        #region vars
        private ConfigData configData;
        private const string codeLockPrefab = "assets/prefabs/locks/keypad/lock.code.prefab";
        public Quaternion entityrot;
        public Vector3 entitypos;
        public BaseEntity newlock;

        public Dictionary<ulong, bool> userenabled = new Dictionary<ulong, bool>();
        public Dictionary<int, coffinpair> coffinpairs = new Dictionary<int, coffinpair>();
        public List<ulong> coffins = new List<ulong>();
        private const string permCoffinlockUse = "coffinlock.use";
        private const string permCoffinlockAdmin = "coffinlock.admin";

        public class coffinpair
        {
            public ulong owner;
            public ulong coffinid;
            public ulong lockid;
        }
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        #region init
        private void Init()
        {
            AddCovalenceCommand("cl", "cmdCoffinLock");

            permission.RegisterPermission(permCoffinlockUse, this);
            permission.RegisterPermission(permCoffinlockAdmin, this);

            LoadData();
        }

        private void Loaded() => LoadConfigValues();

        public class ConfigData
        {
            public Settings Settings;
            public VersionNumber Version;
        }

        public class Settings
        {
            [JsonProperty(PropertyName = "Owner can bypass lock")]
            public bool ownerBypass;

            [JsonProperty(PropertyName = "Admin can bypass lock")]
            public bool adminBypass;

            public bool debug;
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file");
            configData = new ConfigData
            {
                Settings = new Settings
                {
                    ownerBypass = false,
                    adminBypass = false,
                    debug = false
                },
                Version = Version
            };
            SaveConfig(configData);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private void LoadConfigValues()
        {
            configData = Config.ReadObject<ConfigData>();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cannotdo"] = "You cannot remove a lock which is part of a coffin.",
                ["cannotdo2"] = "You cannot add a lock to an already locked coffin.",
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

        private void Unload()
        {
            SaveData();
        }

        private void LoadData()
        {
            userenabled = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, bool>>(Name + "/coffinlock_user");
            coffinpairs = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<int, coffinpair>>(Name + "/coffinlock_data");
            foreach (KeyValuePair<int, coffinpair> coffindata in coffinpairs)
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
            if (entity == null) return;
            if (entity.name.Contains("coffin"))
            {
                BasePlayer player = FindOwner(entity.OwnerID);
                if (player == null)
                {
                    if (configData.Settings.debug) Puts("Could not find owner of this coffin.");
                    return;
                }

                if (!permission.UserHasPermission(player.UserIDString, permCoffinlockUse))
                {
                    if (configData.Settings.debug) Puts($"Player {player.displayName} denied permission.");
                    return;
                }
                if (!userenabled.ContainsKey(player.userID))
                {
                    if (configData.Settings.debug) Puts($"Player {player.displayName} has never enabled CoffinLock.");
                    Message(player.IPlayer, "disabled");
                    return;
                }
                if (!userenabled[player.userID])
                {
                    if (configData.Settings.debug) Puts($"Player {player.displayName} has CoffinLock disabled.");
                    Message(player.IPlayer, "disabled");
                    return;
                }

                if (AddLock(entity))
                {
                    coffins.Add((uint)entity.net.ID.Value);
                    Message(player.IPlayer, "spawned");
                    SaveData();
                    if (configData.Settings.debug) Puts("Spawned coffin with lock");
                }
                else
                {
                    if (configData.Settings.debug) Puts("Failed to spawn coffin with lock");
                    Message(player.IPlayer, "failed");
                }
            }
            else if (entity.name.Contains("dropbox"))
            {
                BasePlayer player = FindOwner(entity.OwnerID);
                if (player == null)
                {
                    if (configData.Settings.debug) Puts("Could not find owner of this dropbox.");
                    return;
                }

                if (!permission.UserHasPermission(player.UserIDString, permCoffinlockUse))
                {
                    if (configData.Settings.debug) Puts($"Player {player.displayName} denied permission.");
                    return;
                }
                if (!userenabled.ContainsKey(player.userID))
                {
                    if (configData.Settings.debug) Puts($"Player {player.displayName} has never enabled CoffinLock.");
                    Message(player.IPlayer, "disabled");
                    return;
                }
                if (!userenabled[player.userID])
                {
                    if (configData.Settings.debug) Puts($"Player {player.displayName} has CoffinLock disabled.");
                    Message(player.IPlayer, "disabled");
                    return;
                }

                if (AddLock(entity, true))
                {
                    coffins.Add((uint)entity.net.ID.Value);
                    Message(player.IPlayer, "dspawned");
                    SaveData();
                    if (configData.Settings.debug) Puts("Spawned dropbox with lock");
                }
                else
                {
                    if (configData.Settings.debug) Puts("Failed to spawn dropbox with lock");
                    Message(player.IPlayer, "dfailed");
                }
            }
        }

        private BaseEntity CheckParent(BaseEntity entity)
        {
            if (entity.HasParent())
            {
                BaseEntity parententity = entity.GetParentEntity();
                if (parententity is MiningQuarry)
                {
                    entity.OwnerID = parententity.OwnerID;
                    entity = parententity;
                }
            }
            return entity;
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            BaseEntity entity = container;
            if (entity == null) return null;
            BaseEntity myent = CheckParent(entity);
            if (myent == null) return null;

            if (IsOurcoffin((uint)myent.net.ID.Value))
            {
                if (myent.name.Contains("coffin"))
                {
                    if (IsLocked((uint)myent.net.ID.Value))
                    {
                        if (configData.Settings.debug) Puts("CanLootEntity: Player trying to open our locked coffin!");
                        if (myent.OwnerID == player.userID && configData.Settings.ownerBypass)
                        {
                            Puts("CanLootEntity: Per config, owner can bypass");
                            return null;
                        }
                        if (permission.UserHasPermission(player.UserIDString, permCoffinlockAdmin) && configData.Settings.adminBypass)
                        {
                            if (configData.Settings.debug) Puts("CanLootEntity: Per config, admin can bypass.");
                            return null;
                        }

                        Message(player.IPlayer, "locked");
                        return true;
                    }
                    else
                    {
                        if (configData.Settings.debug) Puts("CanLootEntity: Player opening our unlocked coffin!");
                        return null;
                    }
                }
                else if (myent.name.Contains("dropbox"))
                {
                    if (IsLocked((uint)myent.net.ID.Value))
                    {
                        if (configData.Settings.debug) Puts("CanLootEntity: Player trying to open our locked dropbox!");
                        if (myent.OwnerID == player.userID && configData.Settings.ownerBypass)
                        {
                            if (configData.Settings.debug) Puts("CanLootEntity: Per config, owner can bypass");
                            return null;
                        }
                        if (permission.UserHasPermission(player.UserIDString, permCoffinlockAdmin) && configData.Settings.adminBypass)
                        {
                            if (configData.Settings.debug) Puts("CanLootEntity: Per config, admin can bypass.");
                            return null;
                        }

                        Message(player.IPlayer, "dlocked");
                        return true;
                    }
                    else
                    {
                        if (configData.Settings.debug) Puts("CanLootEntity: Player opening our unlocked dropbox!");
                        return null;
                    }
                }
            }
            return null;
        }

        // Check for our coffin
        private object CanPickupEntity(BasePlayer player, BaseEntity myent)
        {
            if (myent == null) return null;
            if (player == null) return null;

            if (IsOurcoffin((uint)myent.net.ID.Value))
            {
                if (myent.name.Contains("coffin"))
                {
                    if (IsLocked((uint)myent.net.ID.Value))
                    {
                        if (configData.Settings.debug) Puts("CanPickupEntity: Player trying to pickup our locked coffin!");
                        Message(player.IPlayer, "locked");
                        return false;
                    }
                    else
                    {
                        if (configData.Settings.debug) Puts("CanPickupEntity: Player picking up our unlocked coffin!");
                        coffins.Remove((uint)myent.net.ID.Value);
                        int mycoffin = coffinpairs.FirstOrDefault(x => x.Value.coffinid == (uint)myent.net.ID.Value).Key;
                        coffinpairs.Remove(mycoffin);
                        SaveData();
                        return null;
                    }
                }
                else if (myent.name.Contains("dropbox"))
                {
                    if (IsLocked((uint)myent.net.ID.Value))
                    {
                        if (configData.Settings.debug) Puts("CanPickupEntity: Player trying to pickup our locked dropbox!");
                        Message(player.IPlayer, "dlocked");
                        return false;
                    }
                    else
                    {
                        if (configData.Settings.debug) Puts("CanPickupEntity: Player picking up our unlocked dropbox!");
                        coffins.Remove((uint)myent.net.ID.Value);
                        int mycoffin = coffinpairs.FirstOrDefault(x => x.Value.coffinid == (uint)myent.net.ID.Value).Key;
                        coffinpairs.Remove(mycoffin);
                        SaveData();
                        return null;
                    }
                }
            }
            return null;
        }

        // Check for our coffin, block adding another lock
        private object CanDeployItem(BasePlayer player, Deployer deployer, NetworkableId entityId)
        {
            if (IsOurcoffin(entityId.Value))
            {
                if (configData.Settings.debug) Puts($"Player {player.displayName} trying to place an item(lock?) on our coffin!");
                Message(player.IPlayer, "cannotdo2");
                return true;
            }

            return null;
        }

        // Check for our coffin lock, block pickup
        private object CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if (baseLock == null) return null;
            if (player == null) return null;

            BaseEntity ecoffin = baseLock.GetParentEntity();
            if (ecoffin == null) return null;

            if ((ecoffin.name.Contains("coffin") || ecoffin.name.Contains("dropbox")) && IsOurcoffin((uint)ecoffin.net.ID.Value, (uint)baseLock.net.ID.Value))
            {
                if (configData.Settings.debug) Puts("CanPickupLock: Player trying to remove lock from a locked coffin/dropbox!");
                Message(player.IPlayer, "cannotdo");
                return false;
            }
            return null;
        }

        private void OnNewSave(string strFilename)
        {
            // Wipe the dict of coffin pairs.  But, player prefs are maintained.
            coffinpairs = new Dictionary<int, coffinpair>();
            SaveData();
        }
        #endregion

        #region Main
        [Command("cl")]
        private void cmdCoffinLock(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permCoffinlockUse)) { Message(player, "notauthorized"); return; }
            ulong playerID = ulong.Parse(player.Id);

            if (args.Length == 0)
            {
                if (!userenabled.ContainsKey(playerID))
                {
                    Message(player, "disabled");
                    Message(player, "instructions");
                }
                else if (!userenabled[playerID])
                {
                    Message(player, "disabled");
                    Message(player, "instructions");
                }
                else if (userenabled[playerID])
                {
                    Message(player, "enabled");
                    Message(player, "instructions");
                }
                return;
            }
            if (args[0] == "on" || args[0] == "1")
            {
                if (!userenabled.ContainsKey(playerID))
                {
                    userenabled.Add(playerID, true);
                }
                else if (!userenabled[playerID])
                {
                    userenabled[playerID] = true;
                }
                Message(player, "enabled");
            }
            else if (args[0] == "off" || args[0] == "0")
            {
                if (!userenabled.ContainsKey(playerID))
                {
                    userenabled.Add(playerID, false);
                }
                else if (userenabled[playerID])
                {
                    userenabled[playerID] = false;
                }
                Message(player, "disabled");
            }
            else if (args[0] == "who" && player.HasPermission(permCoffinlockAdmin))
            {
                RaycastHit hit;
                BasePlayer basePlayer = player.Object as BasePlayer;
                if (Physics.Raycast(basePlayer.eyes.HeadRay(), out hit, 2.2f))
                {
                    BaseEntity ecoffin = hit.GetEntity();
                    BasePlayer owner = FindOwner(ecoffin.OwnerID);
                    Message(player, "owner", owner.displayName);
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
            if (newlock = GameManager.server.CreateEntity(codeLockPrefab, entitypos, entityrot, true))
            {
                if (dropbox)
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
                RemoveComps(newlock);
                newlock?.Spawn();
                newlock.OwnerID = ecoffin.OwnerID;

                int id = UnityEngine.Random.Range(1, 99999999);
                coffinpairs.Add(id, new coffinpair
                {
                    owner = ecoffin.OwnerID,
                    coffinid = (uint)ecoffin.net.ID.Value,
                    lockid = (uint)newlock.net.ID.Value
                });
                return true;
            }
            return false;
        }

        public void RemoveComps(BaseEntity obj)
        {
            UnityEngine.Object.DestroyImmediate(obj.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(obj.GetComponent<GroundWatch>());
            foreach (MeshCollider mesh in obj.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        // Used to find the owner of a coffin
        private BasePlayer FindOwner(ulong playerID)
        {
            if (playerID == 0) return null;
            IPlayer iplayer = covalence.Players.FindPlayer(playerID.ToString());

            if (iplayer != null)
            {
                return iplayer.Object as BasePlayer;
            }
            else
            {
                return null;
            }
        }

        private bool IsOurcoffin(ulong coffinid, ulong lockid = 0)
        {
            if (coffinid == 0) return false;
            foreach (KeyValuePair<int, coffinpair> coffindata in coffinpairs)
            {
                if (configData.Settings.debug) Puts($"Checking to see if {coffinid.ToString()} is one of our coffins...");
                if ((coffindata.Value.coffinid == coffinid && (lockid > 0 && coffindata.Value.lockid == lockid))
                    || (coffindata.Value.coffinid == coffinid && lockid == 0))
                {
                    if (configData.Settings.debug) Puts($"This is one of our coffins! {coffinid.ToString()}, {lockid.ToString()}");
                    return true;
                }
            }
            return false;
        }

        // Check whether this coffin has an associated lock, and whether or not it is locked
        private bool IsLocked(ulong coffinid)
        {
            if (coffinid == 0) return false;
            foreach (KeyValuePair<int, coffinpair> coffindata in coffinpairs)
            {
                if (coffindata.Value.coffinid == coffinid)
                {
                    uint mylockid = Convert.ToUInt32(coffindata.Value.lockid);
                    BaseNetworkable bent = BaseNetworkable.serverEntities.Find(new NetworkableId(mylockid));
                    BaseEntity lockent = bent as BaseEntity;
                    if (lockent.IsLocked())
                    {
                        if (configData.Settings.debug) Puts("Found an associated lock!");
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion
    }
}
