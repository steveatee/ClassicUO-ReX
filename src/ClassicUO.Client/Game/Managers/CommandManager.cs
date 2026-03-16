// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Scenes;
using ClassicUO.Input;
using ClassicUO.Resources;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal sealed class CommandManager
    {
        private readonly Dictionary<string, Action<string[]>> _commands = new Dictionary<string, Action<string[]>>();
        private readonly World _world;

        public CommandManager(World world)
        {
            _world = world;
        }

        public void Initialize()
        {
            Register
            (
                "info",
                s =>
                {
                    if (_world.TargetManager.IsTargeting)
                    {
                        _world.TargetManager.CancelTarget();
                    }

                    _world.TargetManager.SetTargeting(CursorTarget.SetTargetClientSide, CursorType.Target, TargetType.Neutral);
                }
            );

            Register
            (
                "datetime",
                s =>
                {
                    if (_world.Player != null)
                    {
                        GameActions.Print(_world, string.Format(ResGeneral.CurrentDateTimeNowIs0, DateTime.Now));
                    }
                }
            );

            Register
            (
                "hue",
                s =>
                {
                    if (_world.TargetManager.IsTargeting)
                    {
                        _world.TargetManager.CancelTarget();
                    }

                    _world.TargetManager.SetTargeting(CursorTarget.HueCommandTarget, CursorType.Target, TargetType.Neutral);
                }
            );


            Register
            (
                "debug",
                s =>
                {
                    CUOEnviroment.Debug = !CUOEnviroment.Debug;

                }
            );

            Register
            (
                "marktile",
                s =>
                {
                    if (s.Length > 1 && s[1] == "-r")
                    {
                        if (s.Length == 2)
                        {
                            TileMarkerManager.Instance.RemoveTile(_world.Player.X, _world.Player.Y, _world.Map.Index);
                        }
                        else if (s.Length == 4)
                        {
                            if (int.TryParse(s[2], out int x) && int.TryParse(s[3], out int y))
                            {
                                TileMarkerManager.Instance.RemoveTile(x, y, _world.Map.Index);
                            }
                        }
                        else if (s.Length == 5)
                        {
                            if (
                                int.TryParse(s[2], out int x)
                                && int.TryParse(s[3], out int y)
                                && int.TryParse(s[4], out int map)
                            )
                            {
                                TileMarkerManager.Instance.RemoveTile(x, y, map);
                            }
                        }
                    }
                    else
                    {
                        if (s.Length == 1)
                        {
                            TileMarkerManager.Instance.AddTile(_world.Player.X, _world.Player.Y, _world.Map.Index, 32);
                        }
                        else if (s.Length == 2)
                        {
                            if (ushort.TryParse(s[1], out ushort hue))
                            {
                                TileMarkerManager.Instance.AddTile(_world.Player.X, _world.Player.Y, _world.Map.Index, hue);
                            }
                        }
                        else if (s.Length == 4)
                        {
                            if (
                                int.TryParse(s[1], out int x)
                                && int.TryParse(s[2], out int y)
                                && ushort.TryParse(s[3], out ushort hue)
                            )
                            {
                                TileMarkerManager.Instance.AddTile(x, y, _world.Map.Index, hue);
                            }
                        }
                        else if (s.Length == 5)
                        {
                            if (
                                int.TryParse(s[1], out int x)
                                && int.TryParse(s[2], out int y)
                                && int.TryParse(s[3], out int map)
                                && ushort.TryParse(s[4], out ushort hue)
                            )
                            {
                                TileMarkerManager.Instance.AddTile(x, y, map, hue);
                            }
                        }
                    }

                    TileMarkerManager.Instance.Save();
                }
            );
        }


        public void Register(string name, Action<string[]> callback)
        {
            name = name.ToLower();

            if (!_commands.ContainsKey(name))
            {
                _commands.Add(name, callback);
            }
            else
            {
                Log.Error($"Attempted to register command: '{name}' twice.");
            }
        }

        public void UnRegister(string name)
        {
            name = name.ToLower();

            if (_commands.ContainsKey(name))
            {
                _commands.Remove(name);
            }
        }

        public void UnRegisterAll()
        {
            _commands.Clear();
        }

        public void Execute(string name, params string[] args)
        {
            name = name.ToLower();

            if (_commands.TryGetValue(name, out Action<string[]> action))
            {
                action.Invoke(args);
            }
            else
            {
                Log.Warn($"Command: '{name}' not exists");
            }
        }

        public void OnHueTarget(Entity entity)
        {
            if (entity != null)
            {
                _world.TargetManager.Target(entity);
                Mouse.LastLeftButtonClickTime = 0;
                GameActions.Print(_world, string.Format(ResGeneral.ItemID0Hue1, entity.Graphic, entity.Hue));
            }
            else
            {
                Mouse.LastLeftButtonClickTime = 0;
            }
        }
    }
}
