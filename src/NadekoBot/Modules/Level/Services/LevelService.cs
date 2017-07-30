﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NadekoBot.Services;
using NadekoBot.Services.Database.Repositories;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace NadekoBot.Modules.Level.Services
{
    public class LevelService : INService
    {
        public readonly DbService _db;
        public readonly CommandService _cmds;
        public LevelService(DiscordSocketClient client, DbService db, CommandService cmds)
        {
            _db = db;
            client.MessageReceived += OnMessageReceived;
            client.MessageUpdated += OnMessageUpdated;
            client.MessageDeleted += OnMessageDeleted;
            client.MessageReceived += AddLevelRole;
        }

        private Task AddLevelRole(SocketMessage sm)
        {
            _log.Info($"AddLevelRole Start");
            var user = (SocketGuildUser)sm.Author;
            IEnumerable<IRole> rolesToAdd;
            using (var uow = _db.UnitOfWork)
            {
            _log.Info($"AddLevelRole Getting RoleBindings");
                var rlb = uow.RoleLevelBinding.RoleLevelBindings.Where(rl => rl.MinimumLevel <= uow.LevelModel.GetLevel(user.Id) && user.Roles.FirstOrDefault(r => r.Id == rl.RoleId) == null);
                rolesToAdd = user.Guild.Roles.Where(r => rlb.FirstOrDefault(rl => rl.RoleId == r.Id) != null);
                _log.Info($"AddLevelRole GottenRoleBindings");
            }
            if (rolesToAdd.Count() == 0) return Task.CompletedTask;
            _log.Info($"AddLevelRole Adding Roles");
            user.AddRolesAsync(rolesToAdd);
            var rolestring = "\"";
            _log.Info($"AddLevelRole Making String");
            foreach (var role in rolesToAdd)
            {
                rolestring += role.Name + "\", \"";
            }
            rolestring = rolestring.Substring(0, rolestring.Length - 3) + "\"";
            sm.Channel.SendMessageAsync($"{user.Mention} hat die Rolle{(rolesToAdd.Count() > 1 ? "n" : "")} {rolestring} bekommen.");
            return Task.CompletedTask;
        }

        private Task OnMessageReceived(SocketMessage sm)
        {
            if (sm.Content.Length < 10 || sm.Author.IsBot) return Task.CompletedTask;
            using (var uow = _db.UnitOfWork)
            {
                var time = DateTime.Now;
                if (!uow.LevelModel.CanGetMessageXP(sm.Author.Id, time)) return Task.CompletedTask;
                uow.LevelModel.TryAddXP(sm.Author.Id, sm.Content.Length > 25 ? 25 : sm.Content.Length, false);
                uow.LevelModel.ReplaceTimestamp(sm.Author.Id, time);
                SendLevelChangedMessage(uow.LevelModel.CalculateLevel(sm.Author.Id), sm.Author, sm.Channel);
                uow.Complete();
            }
            return Task.CompletedTask;
        }

        private Task OnMessageUpdated(Cacheable<IMessage, ulong> um, SocketMessage sm, ISocketMessageChannel smc)
        {
            if (!um.HasValue || um.Value.Author.IsBot || (um.Value.Content.Length > 25 && sm.Content.Length > 25) || (um.Value.Content.Length < 10 && sm.Content.Length < 10)) return Task.CompletedTask;
            using (var uow = _db.UnitOfWork)
            {
                uow.LevelModel.TryAddXP(um.Value.Author.Id, sm.Content.Length - um.Value.Content.Length, false);
                SendLevelChangedMessage(uow.LevelModel.CalculateLevel(sm.Author.Id), sm.Author, smc);
                uow.Complete();
            }
            return Task.CompletedTask;
        }

        private Task OnMessageDeleted(Cacheable<IMessage, ulong> um, ISocketMessageChannel smc)
        {
            if (!um.HasValue || um.Value.Author.IsBot || um.Value.Content.Length < 10 || _cmds.Commands.Any(c => um.Value.Content.StartsWith(c.Name + " ") || c.Aliases.Any(c2 => um.Value.Content.StartsWith(c2)))) return Task.CompletedTask;
            using (var uow = _db.UnitOfWork)
            {
                uow.LevelModel.TryAddXP(um.Value.Author.Id, um.Value.Content.Length > 25 ? -25 : -um.Value.Content.Length);
                SendLevelChangedMessage(uow.LevelModel.CalculateLevel(um.Value.Author.Id), um.Value.Author, smc);
                uow.Complete();
            }
            return Task.CompletedTask;
        }

        private void SendLevelChangedMessage(CalculatedLevel cl, IUser user, ISocketMessageChannel smc)
        {
            if (cl.IsNewLevelHigher)
            {
                smc.SendMessageAsync($"Herzlichen Glückwunsch { user.Mention }, du bist von Level { cl.OldLevel } auf Level { cl.NewLevel } aufgestiegen!");
            }
            else if (cl.IsNewLevelLower)
            {
                smc.SendMessageAsync($"Schade { user.Mention }, du bist von Level { cl.OldLevel } auf Level { cl.NewLevel } abgestiegen :(");
            }
        }
    }
}
