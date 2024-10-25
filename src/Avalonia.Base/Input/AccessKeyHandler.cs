using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
        private readonly string? _key;

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
            RoutedEvent = AccessKeyHandler.AccessKeyPressedEvent;
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
            RoutedEvent = AccessKeyHandler.AccessKeyEvent;

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

    internal record AccessKeyRegistration(string Key, WeakReference<IInputElement> Target)
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
        public static readonly RoutedEvent<AccessKeyEventArgs> AccessKeyEvent =
            RoutedEvent.Register<AccessKeyEventArgs>(
                "AccessKey",
                RoutingStrategies.Bubble,
                typeof(AccessKeyHandler));

        /// <summary>
        /// Defines the AccessKeyPressed attached event.
        /// </summary>
        public static readonly RoutedEvent<AccessKeyPressedEventArgs> AccessKeyPressedEvent =
            RoutedEvent.Register<AccessKeyPressedEventArgs>(
                "AccessKeyPressed",
                RoutingStrategies.Bubble,
                typeof(AccessKeyHandler));

        /// <summary>
        /// The registered access keys.
        /// </summary>
        private readonly List<AccessKeyRegistration> _registered = new();

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
        private WeakReference<IInputElement>? _restoreFocusElement;

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
                var registrationsToRemove = _registered.Where(m =>
                        m.Key == key && m.GetInputElement() == null)
                    .ToList();

                foreach (var registration in registrationsToRemove)
                {
                    _registered.Remove(registration);
                }

                _registered.Add(new AccessKeyRegistration(key, new WeakReference<IInputElement>(element)));
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
                var registrationsToRemove = _registered
                    .Where(m =>
                    {
                        var inputElement = m.GetInputElement();
                        return inputElement == null || inputElement == element;
                    })
                    .ToList();

                foreach (var mapping in registrationsToRemove)
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
            // if the owner (IInputRoot) does not have the keyboard focus, ignore all keyboard events
            // KeyboardDevice.IsKeyboardFocusWithin in case of a PopupRoot seems to only work once, so we created our own
            var isFocusWithinOwner = IsFocusWithinOwner(_owner!);
            if (!isFocusWithinOwner)
                return;

            if (e.Key is Key.LeftAlt or Key.RightAlt)
            {
                _altIsDown = true;

                if (MainMenu is not { IsOpen: true })
                {
                    var focusManager = FocusManager.GetFocusManager(e.Source as IInputElement);

                    // TODO: Use FocusScopes to store the current element and restore it when context menu is closed.
                    // Save currently focused input element.
                    var focusedElement = focusManager?.GetFocusedElement();
                    if (focusedElement is not null)
                        _restoreFocusElement = new WeakReference<IInputElement>(focusedElement);

                    // When Alt is pressed without a main menu, or with a closed main menu, show
                    // access key markers in the window (i.e. "_File").
                    _owner!.ShowAccessKeys = _showingAccessKeys = isFocusWithinOwner;
                }
                else
                {
                    // If the Alt key is pressed and the main menu is open, close the main menu.
                    CloseMenu();
                    _ignoreAltUp = true;

                    if (_restoreFocusElement?.TryGetTarget(out var restoreElement) ?? false)
                    {
                        Dispatcher.UIThread.Post(() => restoreElement.Focus());
                    }
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
            // if the owner (IInputRoot) does not have the keyboard focus, ignore all keyboard events
            // KeyboardDevice.IsKeyboardFocusWithin in case of a PopupRoot seems to only work once, so we created our own
            var isFocusWithinOwner = IsFocusWithinOwner(_owner!);
            if (!isFocusWithinOwner)
                return;

            if (!e.KeyModifiers.HasAllFlags(KeyModifiers.Alt) || e.KeyModifiers.HasAllFlags(KeyModifiers.Control))
            {
                if (MainMenu?.IsOpen != true)
                {
                    return;
                }
            }

            var key = NormalizeKey(e.Key.ToString());
            var targets = SortByHierarchy(GetTargetsForSender(e.Source as IInputElement, key));
            e.Handled = ProcessKey(targets, key, false) != ProcessKeyResult.NoMatch;
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
                //lastWasFocused = FocusManager.GetFocusManager(_owner)?.GetFocusedElement() == target;
            }

            if (effectiveTarget != null)
            {
                var args = new AccessKeyEventArgs(key, isMultiple: !isSingleTarget || existsElsewhere);
                effectiveTarget.RaiseEvent(args);

                return chosenIndex == targets.Count - 1 ? ProcessKeyResult.LastMatch : ProcessKeyResult.MoreMatches;
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
                var registrationsToRemove = _registered
                    .Where(m => m.GetInputElement() == null)
                    .ToList();

                foreach (var registration in registrationsToRemove)
                {
                    _registered.Remove(registration);
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

        private static List<IInputElement> SortByHierarchy(List<IInputElement> targets)
        {
            var sorted = new List<IInputElement>();
            var elements = targets.OfType<InputElement>().ToList();
            for (var i = 0; i < elements.Count; i++)
            {
                var parent = elements[i];
                if (sorted.Contains(parent))
                    continue;
                
                sorted.Add(parent);
                for (var j = i + 1; j < elements.Count; j++)
                {
                    var current = elements[j];
                    if (parent.IsLogicalAncestorOf(current))
                    {
                        sorted.Add(current);
                    }
                }
            }

            return sorted;
        }

        private bool IsFocusWithinOwner(IInputRoot owner)
        {
            var focusedElement = KeyboardDevice.Instance?.FocusedElement;
            if (focusedElement is not InputElement inputElement)
                return false;

            var isAncestorOf = owner is Visual root && root.IsVisualAncestorOf(inputElement);
            return isAncestorOf;
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

/**
 * ProcessKey => raises AccessKeyEvent: sets the Focus
 * GetElementInfo => raises AccessKeyPressedEvent: gets the target
 */
