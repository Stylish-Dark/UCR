using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Annotations;

namespace HidWizards.UCR.Core.Models
{
    public sealed class ProfileApplicationRule : INotifyPropertyChanged
    {
        private string _executable;
        private string _arguments;

        [XmlAttribute]
        public string Executable
        {
            get => _executable;
            set
            {
                if (string.Equals(_executable, value, StringComparison.Ordinal)) return;
                _executable = value;
                OnPropertyChanged();
                Profile?.Context?.ContextChanged();
            }
        }

        [XmlAttribute]
        public string Arguments
        {
            get => _arguments;
            set
            {
                if (string.Equals(_arguments, value, StringComparison.Ordinal)) return;
                _arguments = value;
                OnPropertyChanged();
                Profile?.Context?.ContextChanged();
            }
        }

        [XmlIgnore]
        internal Profile Profile { get; private set; }

        public ProfileApplicationRule()
        {
        }

        public ProfileApplicationRule(string executable, string arguments = null)
        {
            _executable = executable;
            _arguments = arguments;
        }

        internal void Attach(Profile profile)
        {
            Profile = profile;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
