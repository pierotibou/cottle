using System;
using Cottle.Values;
using NUnit.Framework;

namespace Cottle.Test
{
	[TestFixture]
	public class ValueTester
	{
		[Test]
		public void ImplicitConstructor ()
		{
			ValueTester.Compare (() => "Hello, World!", new StringValue ("Hello, World!"));
			ValueTester.Compare (() => 5.3, new NumberValue (5.3));
			ValueTester.Compare (() => 5, new NumberValue (5));
			ValueTester.Compare (() => double.PositiveInfinity, VoidValue.Instance);
			ValueTester.Compare (() => double.NaN, VoidValue.Instance);
			ValueTester.Compare (() => float.NegativeInfinity, VoidValue.Instance);
			ValueTester.Compare (() => float.NaN, VoidValue.Instance);
			ValueTester.Compare (() => true, new BooleanValue (true));
		}

		private static void Compare (Func<Value> constructor, Value expected)
		{
			Assert.AreEqual (expected, constructor ());
		}
	}
}
