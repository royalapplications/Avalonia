using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avalonia.Controls
{
    /// <summary>
    /// Handles access keys within a <see cref="MenuItem"/>
    /// </summary>
    internal class MenuItemAccessKeyHandler : AccessKeyHandler
    {
        protected override void OnSetOwner(IInputRoot owner)
        {
            owner.AddHandler(InputElement.TextInputEvent, OnTextInput);
        }

        /// <summary>
        /// Handles a key being pressed in the menu.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        protected virtual void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Text))
            {
                var key = e.Text;
                var registration = Registrations
                    .FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                
                if (registration == null)
                    return;

                e.Handled = ProcessKey(registration.GetInputElement(), key);
            }
        }
    }
}
