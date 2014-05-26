﻿using Cottle.Values;

namespace Cottle.Scopes
{
	public abstract class AbstractScope : IScope
	{
		#region Properties

		public Value	this[Value symbol]
		{
			get
			{
				Value	value;

				if (this.Get (symbol, out value))
					return value;

				return VoidValue.Instance;
			}
			set
			{
				this.Set (symbol, value, ScopeMode.Closest);
			}
		}

		#endregion

		#region Methods
		
		public abstract void	Enter ();
		
		public abstract bool	Get (Value symbol, out Value value);
		
		public abstract bool	Leave ();

		public abstract void	Set (Value symbol, Value value, ScopeMode mode);

		#endregion
	}
}