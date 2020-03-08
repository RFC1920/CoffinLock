//#define DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Rust;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Coffin Lock", "RFC1920", "1.0.1")]
    [Description("Lock coffins with a code lock")]
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

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cannotdo"] = "You cannot remove a lock which is part of a coffin.",
                ["notauthorized"] = "You don't have permission to use this command.",
                ["instructions"] = "/cl on to enable, /cl off to disable.",
                ["spawned"] = "CoffinLock spawned a new lockable coffin!",
                ["failed"] = "CoffinLock failed to spawn a new lockable coffin!",
                ["locked"] = "This coffin is locked!",
                ["unlocked"] = "This coffin is unlocked!",
                ["disabled"] = "CoffinLock is disabled.",
                ["enabled"] = "CoffinLock is enabled.",
                ["owner"] = "Coffin owned by {0}.",
                ["nocoffin"] = "Could not find a coffin in front of you."
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
                if(userenabled[player.userID] == false || userenabled[player.userID] == null)
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
            BaseEntity myent = CheckParent(entity);
            if(myent.name.Contains("coffin") && IsOurcoffin(myent.net.ID))
            {
                if(IsLocked(myent.net.ID))
                {
#if DEBUG
                    Puts("CanPickupEntity: player trying to open our locked coffin!");
#endif
                    Message(player.IPlayer, "locked");
                    return false;
                }
                else
                {
#if DEBUG
                    Puts("CanPickupEntity: player opening our unlocked coffin!");
#endif
                    return null;
                }
            }
            return null;
        }

        // Check for our coffin
        private object CanPickupEntity(BasePlayer player, BaseEntity myent)
        {
            if(myent == null) return null;
            if(player == null) return null;

            if(myent.name.Contains("coffin") && IsOurcoffin(myent.net.ID))
            {
                if(IsLocked(myent.net.ID))
                {
#if DEBUG
                    Puts("CanPickupEntity: player trying to pickup our locked coffin!");
#endif
                    Message(player.IPlayer, "locked");
                    return false;
                }
                else
                {
#if DEBUG
                    Puts("CanPickupEntity: player picking up our unlocked coffin!");
#endif
                    coffins.Remove(myent.net.ID);
                    int mycoffin = coffinpairs.FirstOrDefault(x => x.Value.coffinid == myent.net.ID).Key;
                    coffinpairs.Remove(mycoffin);
                    SaveData();
                    return null;
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

            if(ecoffin.name.Contains("coffin") && IsOurcoffin(ecoffin.net.ID))
            {
#if DEBUG
                Puts("CanPickupLock: player trying to remove lock from a locked coffin!");
#endif
                Message(player.IPlayer, "cannotdo");
                return false;
            }
            if(ecoffin.name.Contains("fuel_gen") && IsOurcoffin(ecoffin.net.ID))
            {
#if DEBUG
                Puts("CanPickupLock: player trying to remove lock from a locked generator!");
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
        private bool AddLock(BaseEntity ecoffin)
        {
            newlock = new BaseEntity();
            if(newlock = GameManager.server.CreateEntity(codeLockPrefab, entitypos, entityrot, true))
            {
                newlock.transform.localEulerAngles = new Vector3(0, 96, 0);
                newlock.transform.localPosition = new Vector3(-0.15f, 0.35f, 0.4f);

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
            if(playerID == null) return null;
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
            if(coffinid == null) return false;
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
            if(coffinid == null) return false;
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
