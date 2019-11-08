using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Common
{
	public class RelayCommand : ICommand
	{
		private Action<object> m_onExecute;
		private Func<object, bool> m_onCanExecute;

		public event EventHandler CanExecuteChanged = ( sender, e ) => { };

		public RelayCommand( Action<object> onExecute, Func<object, bool> onCanExecute = null )
		{
			m_onExecute = onExecute;
			m_onCanExecute = onCanExecute;
		}

		public bool CanExecute( object parameter )
		{
			return this.m_onCanExecute == null || this.m_onCanExecute(parameter);
		}

		public void Execute( object parameter )
		{
			m_onExecute?.Invoke(parameter);
		}
	}
}
