using System;
using System.Collections.Generic;

namespace ModUtils
{
    /// <summary>
    /// One row of a choice window (<see cref="ConfigurationGui.OpenChoiceWindow"/>): a name and a dropdown
    /// whose options and selection come from the caller, and the callback that takes the player's choice.
    /// </summary>
    public sealed class ConfigurationGuiChoice
    {
        /// <summary>
        /// Describes a row. The window reads <paramref name="options"/> and <paramref name="selected"/> when
        /// it opens, after every choice and filter change, and on
        /// <see cref="ConfigurationGuiChoiceWindow.Refresh"/>.
        /// </summary>
        /// <param name="name">The row's name, already translated.</param>
        /// <param name="description">
        /// The text shown below the list while the pointer is over the row, already translated, or null or
        /// empty for none.
        /// </param>
        /// <param name="options">The option texts, in order, already translated. Null shows no option.</param>
        /// <param name="selected">
        /// The index of the option that the dropdown shows. An index outside the options shows an empty
        /// caption.
        /// </param>
        /// <param name="choose">
        /// Called with the index into the current options when the player picks an option other than the
        /// shown one. Returns null when the choice is taken, or a message, already translated, that the
        /// window shows in its status line when it is refused.
        /// </param>
        public ConfigurationGuiChoice(string name, string description, Func<IReadOnlyList<string>> options,
            Func<int> selected, Func<int, string> choose)
        {
            Name = name ?? "";
            Description = description ?? "";
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Selected = selected ?? throw new ArgumentNullException(nameof(selected));
            Choose = choose ?? throw new ArgumentNullException(nameof(choose));
        }

        /// <summary>
        /// The row's name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The text shown below the list while the pointer is over the row; empty for none.
        /// </summary>
        public string Description { get; }

        internal Func<IReadOnlyList<string>> Options { get; }

        internal Func<int> Selected { get; }

        internal Func<int, string> Choose { get; }
    }

    /// <summary>
    /// A window that <see cref="ConfigurationGui.OpenChoiceWindow"/> opened. It stays usable after the
    /// window closes, and then does nothing.
    /// </summary>
    public sealed class ConfigurationGuiChoiceWindow
    {
        private readonly ConfigurationGuiChoiceView _view;

        internal ConfigurationGuiChoiceWindow(ConfigurationGuiChoiceView view)
        {
            _view = view;
        }

        /// <summary>
        /// True until the window closes, whether through its Close button, Escape, the gamepad back button,
        /// <see cref="Close"/>, a callback that threw, or the end of the world session.
        /// </summary>
        public bool IsOpen => _view != null && !_view.IsClosed;

        /// <summary>
        /// Closes the window without asking. Can be called from inside the window's callbacks; does nothing
        /// when the window is closed. The game still takes no player input on the frame on which the window
        /// closes.
        /// </summary>
        public void Close()
        {
            if (IsOpen)
                _view.CloseWindow();
        }

        /// <summary>
        /// Reads every row's options and selection again and shows them, for example after the values they
        /// come from changed elsewhere. Never calls a row's choose callback; does nothing when the window is
        /// closed. A row's choose callback and the filter callback may call it, but an options or selected
        /// callback must not, because those run while the rows are read.
        /// </summary>
        public void Refresh()
        {
            if (IsOpen)
                _view.ShowChoices();
        }
    }
}
