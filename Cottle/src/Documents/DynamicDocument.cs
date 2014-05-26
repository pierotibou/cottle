﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cottle.Documents.Dynamic;
using Cottle.Parsers;
using Cottle.Settings;
using Cottle.Values;

namespace Cottle.Documents
{
	/// <summary>
	/// Dynamic document compiles template using MSIL generation for better
	/// performance. Code generated by JIT compiler can be reclaimed by garbage
	/// collector, but you should use a caching mechanism to avoid re-creating
	/// too many DynamicDocument instances using the same template source.
	/// </summary>
	public sealed class DynamicDocument : AbstractDocument
	{
		#region Attributes

		private readonly Renderer	renderer;

		private readonly string[]	strings;

		private readonly Value[]	values;

		#endregion

		#region Constructors

		public DynamicDocument (TextReader reader, ISetting setting)
		{
			Allocator		allocator;
			Label			end;
			DynamicMethod	method;
			IParser			parser;
			Command			root;

			method = new DynamicMethod (string.Empty, typeof (Value), new [] {typeof (DynamicDocument), typeof (IScope), typeof (TextWriter)}, this.GetType ());
			parser = new DefaultParser (setting.BlockBegin, setting.BlockContinue, setting.BlockEnd);

			allocator = new Allocator (method.GetILGenerator ());
			root = parser.Parse (reader);

			this.CompileCommand (allocator, setting.Trimmer, root);

			end = allocator.Generator.DefineLabel ();

			allocator.Generator.Emit (OpCodes.Brtrue, end);
			allocator.Generator.Emit (OpCodes.Pop);

			this.EmitVoid (allocator);

			allocator.Generator.MarkLabel (end);
			allocator.Generator.Emit (OpCodes.Ret);

			this.renderer = (Renderer)method.CreateDelegate (typeof (Renderer));
			this.strings = allocator.Strings.ToArray ();
			this.values = allocator.Values.ToArray ();
		}

		public DynamicDocument (TextReader reader) :
			this (reader, DefaultSetting.Instance)
		{
		}

		public DynamicDocument (string template, ISetting setting) :
			this (new StringReader (template), setting)
		{
		}

		public DynamicDocument (string template) :
			this (new StringReader (template), DefaultSetting.Instance)
		{
		}

		#endregion

		#region Methods / Public

		public override Value Render (IScope scope, TextWriter writer)
		{
			return this.renderer (this, scope, writer);
		}

		#endregion

		#region Methods / Private

		private void CompileCommand (Allocator allocator, Trimmer trimmer, Command command)
		{
			Label	bubble;
			Label	exit;
			Label	next;

			exit = allocator.Generator.DefineLabel ();
			next = allocator.Generator.DefineLabel ();

			switch (command.Type)
			{
				case CommandType.AssignFunction:
					throw new NotImplementedException ();

				case CommandType.AssignValue:
					this.EmitPushScope (allocator);
					this.EmitValue (allocator, command.Name);
					this.CompileExpression (allocator, command.Source);

					allocator.Generator.Emit (OpCodes.Ldc_I4, (int)command.Mode);
					allocator.Generator.Emit (OpCodes.Callvirt, Resolver.Method<Action<IScope, Value, Value, ScopeMode>> ((scope, symbol, value, mode) => scope.Set (symbol, value, mode)));

					break;

				case CommandType.Composite:
					bubble = allocator.Generator.DefineLabel ();

					for (; command.Type == CommandType.Composite; command = command.Next)
					{
						this.CompileCommand (allocator, trimmer, command.Body);

						allocator.Generator.Emit (OpCodes.Brtrue, bubble);
						allocator.Generator.Emit (OpCodes.Pop);
					}

					this.CompileCommand (allocator, trimmer, command);

					allocator.Generator.Emit (OpCodes.Brtrue, bubble);
					allocator.Generator.Emit (OpCodes.Pop);
					allocator.Generator.Emit (OpCodes.Br, next);

					allocator.Generator.MarkLabel (bubble);
					allocator.Generator.Emit (OpCodes.Ldc_I4_1);
					allocator.Generator.Emit (OpCodes.Br, exit);

					break;

				case CommandType.Dump:
					this.EmitPushOutput (allocator);
					this.CompileExpression (allocator, command.Source);

					allocator.Generator.Emit (OpCodes.Callvirt, Resolver.Method<Action<TextWriter, object>> ((writer, value) => writer.Write (value)));

					break;

				case CommandType.Echo:
					this.EmitPushOutput (allocator);
					this.CompileExpression (allocator, command.Source);

					allocator.Generator.Emit (OpCodes.Callvirt, Resolver.Property<Func<Value, string>> ((value) => value.AsString).GetGetMethod ());

					this.EmitCallWriteString (allocator);

					break;

				case CommandType.For:
					throw new NotImplementedException ();

				case CommandType.If:
					throw new NotImplementedException ();

				case CommandType.Literal:
					this.EmitPushOutput (allocator);
					this.EmitString (allocator, trimmer (command.Text));
					this.EmitCallWriteString (allocator);

					break;

				case CommandType.Return:
					this.CompileExpression (allocator, command.Source);

					allocator.Generator.Emit (OpCodes.Ldc_I4_1);

					return;

				case CommandType.While:
					throw new NotImplementedException ();
			}

			allocator.Generator.MarkLabel (next);

			this.EmitVoid (allocator);

			allocator.Generator.Emit (OpCodes.Ldc_I4_0);
			allocator.Generator.MarkLabel (exit);
		}

		private void CompileExpression (Allocator allocator, Expression expression)
		{
			ConstructorInfo	constructor;
			Label			failure;
			LocalBuilder	localArray;
			LocalBuilder	localCaller;
			LocalBuilder	localException;
			Label			success;

			switch (expression.Type)
			{
				case ExpressionType.Access:
					success = allocator.Generator.DefineLabel ();

					// Evaluate source expression and get fields
					this.CompileExpression (allocator, expression.Source);

					allocator.Generator.Emit (OpCodes.Callvirt, Resolver.Property<Func<Value, IMap>> ((value) => value.Fields).GetGetMethod ());

					// Evaluate subscript expression
					this.CompileExpression (allocator, expression.Subscript);

					// Use subscript to get value from fields
					allocator.Generator.Emit (OpCodes.Ldloca, allocator.LocalValue);
					allocator.Generator.Emit (OpCodes.Callvirt, typeof (IMap).GetMethod ("TryGet"));
					allocator.Generator.Emit (OpCodes.Brtrue, success);

					// Emit void value on error
					this.EmitVoid (allocator);

					allocator.Generator.Emit (OpCodes.Stloc, allocator.LocalValue);

					// Push value on stack
					allocator.Generator.MarkLabel (success);
					allocator.Generator.Emit (OpCodes.Ldloc, allocator.LocalValue);

					break;

				case ExpressionType.Constant:
					this.EmitValue (allocator, expression.Value);

					break;

				case ExpressionType.Invoke:
					localArray = allocator.Generator.DeclareLocal (typeof (Value[]));
					localCaller = allocator.Generator.DeclareLocal (typeof (Value));
					localException = allocator.Generator.DeclareLocal (typeof (Exception));
					failure = allocator.Generator.DefineLabel ();
					success = allocator.Generator.DefineLabel ();

					// Evaluate source expression as a function
					this.CompileExpression (allocator, expression.Source);

					allocator.Generator.Emit (OpCodes.Stloc, localCaller);
					allocator.Generator.Emit (OpCodes.Ldloc, localCaller);
					allocator.Generator.Emit (OpCodes.Callvirt, Resolver.Property<Func<Value, IFunction>> ((value) => value.AsFunction).GetGetMethod ());
					allocator.Generator.Emit (OpCodes.Stloc, allocator.LocalFunction);
					allocator.Generator.Emit (OpCodes.Ldloc, allocator.LocalFunction);
					allocator.Generator.Emit (OpCodes.Brfalse, failure);

					// Create array to store evaluated values 
					allocator.Generator.Emit (OpCodes.Ldc_I4, expression.Arguments.Length);
					allocator.Generator.Emit (OpCodes.Newarr, typeof (Value));
					allocator.Generator.Emit (OpCodes.Stloc, localArray);

					// Evaluate arguments and store into array
					for (int i = 0; i < expression.Arguments.Length; ++i)
					{
						allocator.Generator.Emit (OpCodes.Ldloc, localArray);
						allocator.Generator.Emit (OpCodes.Ldc_I4, i);

						this.CompileExpression (allocator, expression.Arguments[i]);

						allocator.Generator.Emit (OpCodes.Stelem_Ref);
					}

					// Invoke function delegate within exception block
					allocator.Generator.BeginExceptionBlock ();
					allocator.Generator.Emit (OpCodes.Ldloc, allocator.LocalFunction);
					allocator.Generator.Emit (OpCodes.Ldloc, localArray);

					this.EmitPushScope (allocator);
					this.EmitPushOutput (allocator);

					allocator.Generator.Emit (OpCodes.Callvirt, Resolver.Method<Func<IFunction, IList<Value>, IScope, TextWriter, Value>> ((function, arguments, scope, output) => function.Execute (arguments, scope, output)));
					allocator.Generator.Emit (OpCodes.Stloc, allocator.LocalValue);

					// Trigger event handler on exception
					allocator.Generator.BeginCatchBlock (typeof (Exception));
					allocator.Generator.Emit (OpCodes.Stloc, localException);

					this.EmitPushDocument (allocator);

					allocator.Generator.Emit (OpCodes.Ldloc, localCaller);
					allocator.Generator.Emit (OpCodes.Ldstr, "function call raised an exception");
					allocator.Generator.Emit (OpCodes.Ldloc, localException);
					allocator.Generator.Emit (OpCodes.Callvirt, Resolver.Method<Action<DynamicDocument, Value, string, Exception>> ((document, source, message, exception) => document.OnError (source, message, exception)));
					allocator.Generator.Emit (OpCodes.Leave_S, failure);
					allocator.Generator.EndExceptionBlock ();
					allocator.Generator.Emit (OpCodes.Br, success);

					// Emit void value on error
					allocator.Generator.MarkLabel (failure);

					this.EmitVoid (allocator);

					allocator.Generator.Emit (OpCodes.Stloc, allocator.LocalValue);

					// Value is already available on stack
					allocator.Generator.MarkLabel (success);
					allocator.Generator.Emit (OpCodes.Ldloc, allocator.LocalValue);

					break;

				case ExpressionType.Map:
					localArray = allocator.Generator.DeclareLocal (typeof (KeyValuePair<Value, Value>[]));

					// Create array to store evaluated pairs
					allocator.Generator.Emit (OpCodes.Ldc_I4, expression.Elements.Length);
					allocator.Generator.Emit (OpCodes.Newarr, typeof (KeyValuePair<Value, Value>));
					allocator.Generator.Emit (OpCodes.Stloc, localArray);

					// Evaluate elements and store into array 
					constructor = Resolver.Constructor<Func<Value, Value, KeyValuePair<Value, Value>>> ((key, value) => new KeyValuePair<Value, Value> (key, value));

					for (int i = 0; i < expression.Elements.Length; ++i)
					{
						allocator.Generator.Emit (OpCodes.Ldloc, localArray);
						allocator.Generator.Emit (OpCodes.Ldc_I4, i);
						allocator.Generator.Emit (OpCodes.Ldelema, typeof (KeyValuePair<Value, Value>));

						this.CompileExpression (allocator, expression.Elements[i].Key);
						this.CompileExpression (allocator, expression.Elements[i].Value);

						allocator.Generator.Emit (OpCodes.Newobj, constructor);
						allocator.Generator.Emit (OpCodes.Stobj, typeof (KeyValuePair<Value, Value>));
					}

					// Create value from array
					constructor = Resolver.Constructor<Func<IEnumerable<KeyValuePair<Value, Value>>, Value>> ((pairs) => new MapValue (pairs));

					allocator.Generator.Emit (OpCodes.Ldloc, localArray);
					allocator.Generator.Emit (OpCodes.Newobj, constructor);

					break;

				case ExpressionType.Symbol:
					success = allocator.Generator.DefineLabel ();

					// Get variable from scope
					this.EmitPushScope (allocator);
					this.EmitValue (allocator, expression.Value);

					allocator.Generator.Emit (OpCodes.Ldloca, allocator.LocalValue);
					allocator.Generator.Emit (OpCodes.Callvirt, typeof (IScope).GetMethod ("Get"));
					allocator.Generator.Emit (OpCodes.Brtrue, success);

					// Emit void value on error
					this.EmitVoid (allocator);

					allocator.Generator.Emit (OpCodes.Stloc, allocator.LocalValue);

					// Push value on stack
					allocator.Generator.MarkLabel (success);
					allocator.Generator.Emit (OpCodes.Ldloc, allocator.LocalValue);

					break;

				case ExpressionType.Void:
					this.EmitVoid (allocator);

					break;
			}
		}

		private void EmitCallWriteString (Allocator allocator)
		{
			allocator.Generator.Emit (OpCodes.Callvirt, Resolver.Method<Action<TextWriter, string>> ((writer, value) => writer.Write (value)));
		}

		private void EmitPushDocument (Allocator allocator)
		{
			allocator.Generator.Emit (OpCodes.Ldarg_0);
		}

		private void EmitPushOutput (Allocator allocator)
		{
			allocator.Generator.Emit (OpCodes.Ldarg_2);
		}

		private void EmitPushScope (Allocator allocator)
		{
			allocator.Generator.Emit (OpCodes.Ldarg_1);
		}

		private void EmitString (Allocator allocator, string literal)
		{
			allocator.Generator.Emit (OpCodes.Ldarg_0);
			allocator.Generator.Emit (OpCodes.Ldfld, Resolver.Field<Func<DynamicDocument, string[]>> ((document) => document.strings));
			allocator.Generator.Emit (OpCodes.Ldc_I4, allocator.Allocate (literal));
			allocator.Generator.Emit (OpCodes.Ldelem_Ref);
		}

		private void EmitValue (Allocator allocator, Value constant)
		{
			allocator.Generator.Emit (OpCodes.Ldarg_0);
			allocator.Generator.Emit (OpCodes.Ldfld, Resolver.Field<Func<DynamicDocument, Value[]>> ((document) => document.values));
			allocator.Generator.Emit (OpCodes.Ldc_I4, allocator.Allocate (constant));
			allocator.Generator.Emit (OpCodes.Ldelem_Ref);
		}

		private void EmitVoid (Allocator allocator)
		{
			allocator.Generator.Emit (OpCodes.Call, Resolver.Property<Func<Value>> (() => VoidValue.Instance).GetGetMethod ());
		}

		#endregion
	}
}