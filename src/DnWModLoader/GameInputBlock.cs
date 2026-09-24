using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace DnWModLoader
{
    internal sealed class GameInputBlock
    {
        private readonly HashSet<InputAction> _disabled = new HashSet<InputAction>();
        private readonly List<InputAction> _enabled = new List<InputAction>();
        private bool _active;
        private bool _failed;

        public void Block()
        {
            _active = true;
        }

        public void Maintain()
        {
            if (!_active || _failed) return;
            try
            {
                _enabled.Clear();
                InputSystem.ListEnabledActions(_enabled);
                foreach (var action in _enabled)
                {
                    try
                    {
                        action.Disable();
                        _disabled.Add(action);
                    }
                    catch (Exception e)
                    {
                        ModLoader.Logger.Debug("Could not disable input action " + action.name + " : " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                _failed = true;
                ModLoader.Logger.Warning("Could not block game input: " + e.Message);
            }
            finally
            {
                _enabled.Clear();
            }
        }

        public void Restore()
        {
            _active = false;
            _failed = false;
            if (_disabled.Count == 0) return;
            int restored = 0;
            foreach (var action in _disabled)
            {
                try
                {
                    if (action.enabled || !IsAlive(action)) continue;
                    action.Enable();
                    restored++;
                }
                catch (Exception e)
                {
                    ModLoader.Logger.Debug("Could not re-enable input action " + action.name + ": " + e.Message);
                }
            }
            _disabled.Clear();
            ModLoader.Logger.Debug("Re-enabled " + restored + " game input action(s).");
        }

        private static bool IsAlive(InputAction action)
        {
            var asset = action.actionMap != null ? action.actionMap.asset : null;
            return ReferenceEquals(asset, null) || asset != null;
        }
    }
}
