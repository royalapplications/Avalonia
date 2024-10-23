using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Interactivity;

namespace Avalonia.Input
{
    /// <summary>
    /// The delegate type for handling a FindScope event
    /// </summary>
    public delegate void AccessKeyPressedEventHandler(object sender, AccessKeyPressedEventArgs e);
    
    /// <summary>
    /// The inputs to an AccessKeyPressedEventHandler
    /// </summary>
    public class AccessKeyPressedEventArgs : RoutedEventArgs
    {
        private object? _scope;
        private IInputElement? _target;
        private string? _key;
    
        #region Constructors
    
        /// <summary>
        /// The constructor for AccessKeyPressed event args
        /// </summary>
        public AccessKeyPressedEventArgs()
        {
            RoutedEvent = AccessKeyHandler.AccessKeyPressedEvent;
            _key = null;
        }
    
        /// <summary>
        /// Constructor for AccessKeyPressed event args
        /// </summary>
        /// <param name="key"></param>
        public AccessKeyPressedEventArgs(string key) : this()
        {
            _key = key;
        }
    
        #endregion
    
        #region Public Properties
    
        /// <summary>
        /// The scope for the element that raised this event.
        /// </summary>
        public object? Scope
        {
            get { return _scope; }
            set { _scope = value; }
        }
    
        /// <summary>
        /// Target element for the element that raised this event.
        /// </summary>
        /// <value></value>
        public IInputElement? Target
        {
            get { return _target; }
            set { _target = value; }
        }
    
        /// <summary>
        /// Key that was pressed
        /// </summary>
        /// <value></value>
        public string? Key
        {
            get { return _key; }
        }
    
        #endregion
    
        #region Protected Methods
    
        // /// <summary>
        // /// </summary>
        // /// <param name="genericHandler">The handler to invoke.</param>
        // /// <param name="genericTarget">The current object along the event's route.</param>
        // protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
        // {
        //     var handler = (AccessKeyPressedEventHandler)genericHandler;
        //
        //     handler(genericTarget, this);
        // }
    
        #endregion
    }
    
    /// <summary>
    /// Information pertaining to when the access key associated with an element is pressed
    /// </summary>
    public class AccessKeyEventArgs : RoutedEventArgs
    {
        /// <summary>
        ///
        /// </summary>
        internal AccessKeyEventArgs(string key, bool isMultiple)
        {
            _key = key;
            _isMultiple = isMultiple;
        }
    
        /// <summary>
        /// The key that was pressed which invoked this access key
        /// </summary>
        /// <value></value>
        public string Key
        {
            get { return _key; }
        }
    
        /// <summary>
        /// Were there other elements which are also invoked by this key
        /// </summary>
        /// <value></value>
        public bool IsMultiple
        {
            get { return _isMultiple; }
        }
    
        private string _key;
        private bool _isMultiple;
    }

    internal record AccessKeyMapping(string Key, WeakReference<IInputElement> Target)
    {
        public IInputElement? GetInputElement() =>
            Target.TryGetTarget(out var target) ? target : null;
    }

    // /// <summary>
    // /// Handles access keys for a window.
    // /// </summary>
    internal class AccessKeyHandler : IAccessKeyHandler
    {
        private enum ProcessKeyResult
        {
            NoMatch,
            MoreMatches,
            LastMatch
        }
        
        /// <summary>
        /// Defines the AccessKeyPressed attached event.
        /// </summary>
        public static readonly RoutedEvent<RoutedEventArgs> AccessKeyPressedEvent =
            RoutedEvent.Register<RoutedEventArgs>(
                "AccessKeyPressed",
                RoutingStrategies.Bubble,
                typeof(AccessKeyHandler));

        /// <summary>
        /// The registered access keys.
        /// </summary>
        private readonly List<AccessKeyMapping> _registered = new();

        /// <summary>
        /// The window to which the handler belongs.
        /// </summary>
        private IInputRoot? _owner;

        /// <summary>
        /// Whether access keys are currently being shown;
        /// </summary>
        private bool _showingAccessKeys;

        /// <summary>
        /// Whether to ignore the Alt KeyUp event.
        /// </summary>
        private bool _ignoreAltUp;

        /// <summary>
        /// Whether the AltKey is down.
        /// </summary>
        private bool _altIsDown;

        /// <summary>
        /// Element to restore following AltKey taking focus.
        /// </summary>
        private IInputElement? _restoreFocusElement;

        /// <summary>
        /// The window's main menu.
        /// </summary>
        private IMainMenu? _mainMenu;

        /// <summary>
        /// Gets or sets the window's main menu.
        /// </summary>
        public IMainMenu? MainMenu
        {
            get => _mainMenu;
            set
            {
                if (_mainMenu != null)
                {
                    _mainMenu.Closed -= MainMenuClosed;
                }

                _mainMenu = value;

                if (_mainMenu != null)
                {
                    _mainMenu.Closed += MainMenuClosed;
                }
            }
        }

        /// <summary>
        /// Gets the next element to be focused from the given matches.
        /// If the current element is the last element, the first element will be returned. 
        /// </summary>
        /// <param name="matches">Matched elements with the same accelerator.</param>
        /// <param name="currentFocusedElement">The currently focused element.</param>
        /// <returns>The next element to receive the focus.</returns>
        public static IInputElement? GetNextElementToFocus(IEnumerable<IInputElement> matches,
            IInputElement currentFocusedElement)
        {
            var elements = matches
                .OfType<Visual>()
                .Select(x => x.Parent)
                .Where(m => m != null)
                .OfType<IInputElement>()
                .ToArray();

            for (var i = 0; i < elements.Length; i++)
            {
                var hasNext = i < elements.Length - 1;
                if (elements[i] == currentFocusedElement)
                {
                    // focus the next menu item or the first elem if there is no next element  
                    return hasNext ?
                        elements[i + 1] // next item 
                        :
                        elements[0]; // first item
                }
            }

            return null;
        }

        /// <summary>
        /// Sets the owner of the access key handler.
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <remarks>
        /// This method can only be called once, typically by the owner itself on creation.
        /// </remarks>
        public void SetOwner(IInputRoot owner)
        {
            if (_owner != null)
            {
                throw new InvalidOperationException("AccessKeyHandler owner has already been set.");
            }

            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

            _owner.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
            _owner.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
            _owner.AddHandler(InputElement.KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
            _owner.AddHandler(InputElement.PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        }

        /// <summary>
        /// Registers an input element to be associated with an access key.
        /// </summary>
        /// <param name="accessKey">The access key.</param>
        /// <param name="element">The input element.</param>
        public void Register(char accessKey, IInputElement element)
        {
            var key = NormalizeKey(accessKey.ToString());

            lock (_registered)
            {
                var existing = _registered.Where(m =>
                        m.Key == key && m.GetInputElement() == null)
                    .ToList();

                foreach (var mapping in existing)
                {
                    _registered.Remove(mapping);
                }

                _registered.Add(new AccessKeyMapping(key, new WeakReference<IInputElement>(element)));
            }
        }

        /// <summary>
        /// Unregisters the access keys associated with the input element.
        /// </summary>
        /// <param name="element">The input element.</param>
        public void Unregister(IInputElement element)
        {
            lock (_registered)
            {
                // Get all elements bound to this key and remove this element
                var existing = _registered
                    .Where(m => m.GetInputElement() == null || m.GetInputElement() == element)
                    .ToList();

                foreach (var mapping in existing)
                {
                    _registered.Remove(mapping);
                }
            }
        }

        /// <summary>
        /// Called when a key is pressed in the owner window.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        protected virtual void OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is Key.LeftAlt or Key.RightAlt)
            {
                _altIsDown = true;

                if (MainMenu is not { IsOpen: true })
                {
                    var focusManager = FocusManager.GetFocusManager(e.Source as IInputElement);

                    // TODO: Use FocusScopes to store the current element and restore it when context menu is closed.
                    // Save currently focused input element.
                    _restoreFocusElement = focusManager?.GetFocusedElement();

                    // When Alt is pressed without a main menu, or with a closed main menu, show
                    // access key markers in the window (i.e. "_File").
                    _owner!.ShowAccessKeys = _showingAccessKeys = true;
                }
                else
                {
                    // If the Alt key is pressed and the main menu is open, close the main menu.
                    CloseMenu();
                    _ignoreAltUp = true;

                    _restoreFocusElement?.Focus();
                    _restoreFocusElement = null;
                }
            }
            else if (_altIsDown)
            {
                _ignoreAltUp = true;
            }
        }

        /// <summary>
        /// Called when a key is pressed in the owner window.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        protected virtual void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (!e.KeyModifiers.HasAllFlags(KeyModifiers.Alt) || e.KeyModifiers.HasAllFlags(KeyModifiers.Control))
            {
                if (MainMenu?.IsOpen != true)
                {
                    return;
                }
            }

            var key = NormalizeKey(e.Key.ToString());
            e.Handled = ProcessKeyForSender(e.Source, key, existsElsewhere: false) != ProcessKeyResult.NoMatch;

            // If any other key is pressed with the Alt key held down, or the main menu is open,
            // find all controls who have registered that access key.
            // var text = e.Key.ToString();
            // var matches = _registered
            //     .Where(x => string.Equals(x.AccessKey, text, StringComparison.OrdinalIgnoreCase)
            //                 && x.Element is
            //                 {
            //                     IsEffectivelyVisible: true,
            //                     IsEffectivelyEnabled: true
            //                 })
            //     .Select(x => x.Element);
            //
            //
            // // If the menu is open, only match controls in the menu's visual tree.
            // if (menuIsOpen)
            // {
            //     matches = matches.Where(x => ((Visual)MainMenu!).IsLogicalAncestorOf((Visual)x));
            // }
            //
            // var count = matches.Count();
            // if (count == 1) // If there is a match, raise the AccessKeyPressed event on it.
            // {
            //     // reset the currently selected focus element
            //     _focusElement = null;
            //     var element = matches.FirstOrDefault();
            //     element?.RaiseEvent(new RoutedEventArgs(AccessKeyPressedEvent));
            // }
            // else if (count > 1) // If there are multiple elements, cycle focus through them.
            // {
            //     _focusElement = _focusElement == null ?
            //         (matches.FirstOrDefault() as Visual)?.Parent as IInputElement :
            //         GetNextElementToFocus(matches, _focusElement);
            //
            //     _focusElement?.Focus(NavigationMethod.Tab, KeyModifiers.Alt);
            // }
        }

        /// <summary>
        /// Handles the Alt/F10 keys being released in the window.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        protected virtual void OnPreviewKeyUp(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.LeftAlt:
                case Key.RightAlt:
                    _altIsDown = false;

                    if (_ignoreAltUp)
                    {
                        _ignoreAltUp = false;
                    }
                    else if (_showingAccessKeys && MainMenu != null)
                    {
                        MainMenu.Open();
                    }

                    break;
            }
        }

        /// <summary>
        /// Handles pointer presses in the window.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        protected virtual void OnPreviewPointerPressed(object? sender, PointerEventArgs e)
        {
            if (_showingAccessKeys)
            {
                _owner!.ShowAccessKeys = false;
            }
        }

        /// <summary>
        /// Closes the <see cref="MainMenu"/> and performs other bookeeping.
        /// </summary>
        private void CloseMenu()
        {
            MainMenu!.Close();
            _owner!.ShowAccessKeys = _showingAccessKeys = false;
        }

        private void MainMenuClosed(object? sender, EventArgs e)
        {
            _owner!.ShowAccessKeys = false;
        }

        /// <summary>
        /// Returns StringInfo.GetNextTextElement(key).ToUpperInvariant() throwing exceptions for null
        /// and multi-char strings.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static string NormalizeKey(string key) => key.ToUpperInvariant();

        // Assumes key is already a single unicode character
        private ProcessKeyResult ProcessKeyForSender(object? sender, string key, bool existsElsewhere)
        {
            var targets = GetTargetsForSender(sender as IInputElement, key);
            return ProcessKey(targets, key, existsElsewhere);
        }


        private ProcessKeyResult ProcessKey(List<IInputElement> targets, string key, bool existsElsewhere)
        {
            if (!targets.Any())
                return ProcessKeyResult.NoMatch;

            var isSingleTarget = true;
            var lastWasFocused = false;
            
            IInputElement? effectiveTarget = null;

            int chosenIndex = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];

                if (!IsTargetable(target))
                    continue;

                if (effectiveTarget == null)
                {
                    effectiveTarget = target;
                    chosenIndex = i;
                }
                else
                {
                    if (lastWasFocused)
                    {
                        effectiveTarget = target;
                        chosenIndex = i;
                    }

                    isSingleTarget = false;
                }

                lastWasFocused = target.IsFocused;
                // lastWasFocused = FocusManager.GetFocusManager(_owner)?.GetFocusedElement() == target;
            }

            if (effectiveTarget != null)
            {
                var args = new AccessKeyEventArgs(key, isMultiple: !isSingleTarget || existsElsewhere);
                effectiveTarget.RaiseEvent(args);

                return chosenIndex == targets.Count - 1 
                    ? ProcessKeyResult.LastMatch 
                    : ProcessKeyResult.MoreMatches;
            }

            return ProcessKeyResult.NoMatch;
        }

        /// <summary>
        /// Get the list of access key targets for the sender of the keyboard event.  If sender is null,
        /// pretend key was pressed in the active window.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        private List<IInputElement> GetTargetsForSender(IInputElement? sender, string key)
        {
            // Find the scope for the sender -- will be matched against the possible targets' scopes
            var senderInfo = GetInfoForElement(sender, key);

            return GetTargetsForScope(key, sender, senderInfo);
        }

        private List<IInputElement> GetTargetsForScope(string key, IInputElement? sender,
            AccessKeyInformation senderInfo)
        {
            //Scoping:
            //    1) When key is pressed, find matching AcesssKeys -> S,
            //    3) find scope for keyevent.Source,
            //    4) find scope for everything in S. throw away those that don't match.
            //    5) Final selection uses S.  yay!

            var possibleElements = CopyAndPurgeDead(key);

            if (!possibleElements.Any())
                return [];

            var finalTargets = new List<IInputElement>(1);

            // Go through all the possible elements, find the interesting candidates
            foreach (var element in possibleElements)
            {
                if (element != sender)
                {
                    if (!IsTargetable(element))
                        continue;

                    var elementInfo = GetInfoForElement(element, key);
                    if (elementInfo.Target == null)
                        continue;

                    finalTargets.Add(elementInfo.Target);
                }
                else
                {
                    // This is the same element that sent the event so it must be in the same scope.
                    // Just add it to the final targets
                    if (senderInfo.Target == null)
                        continue;

                    finalTargets.Add(senderInfo.Target);
                }
            }

            return finalTargets;
        }

        private static bool IsTargetable(IInputElement element) =>
            element is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true };

        private List<IInputElement> CopyAndPurgeDead(string key)
        {
            lock (_registered)
            {
                var deadElements = _registered
                    .Where(m => m.GetInputElement() == null)
                    .ToList();

                foreach (var mapping in deadElements)
                {
                    _registered.Remove(mapping);
                }

                return _registered
                    .Where(m => m.Key == key)
                    .Select(m => m.GetInputElement())
                    .OfType<IInputElement>()
                    .ToList();
            }
        }

        /// <summary>
        /// Returns scope for the given element.
        /// </summary>
        /// <param name="element"></param>
        /// <param name="key"></param>
        /// <returns>Scope for the given element, null means the context global scope</returns>
        private AccessKeyInformation GetInfoForElement(IInputElement? element, string key)
        {
            var info = new AccessKeyInformation();
            if (element == null)
                return info;

            var args = new AccessKeyPressedEventArgs(key);
            element.RaiseEvent(args);
            info.Target = args.Target;

            return info;
        }


        private struct AccessKeyInformation
        {
            public IInputElement? Target { get; set; }

            // private static readonly AccessKeyInformation _empty = new();
            //
            //
            // public static AccessKeyInformation Empty
            // {
            //     get
            //     {
            //         return _empty;
            //     }
            // }
        }
    }
}
