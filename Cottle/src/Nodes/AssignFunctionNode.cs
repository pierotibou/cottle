﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Cottle.Expressions;
using Cottle.Functions;
using Cottle.Values;

namespace   Cottle.Nodes
{
    sealed class    AssignFunctionNode : INode
    {
        #region Attributes

        private List<NameExpression>    arguments;

        private INode                   body;

        private ScopeMode               mode;

        private NameExpression          name;

        #endregion

        #region Constructors

        public  AssignFunctionNode (NameExpression name, IEnumerable<NameExpression> arguments, INode body, ScopeMode mode)
        {
            this.arguments = new List<NameExpression> (arguments);
            this.body = body;
            this.mode = mode;
            this.name = name;
        }

        #endregion

        #region Methods

        public bool Render (Scope scope, TextWriter output, out Value result)
        {
            this.name.Set (scope, new FunctionValue (new NodeFunction (this.arguments, this.body)), mode);

            result = UndefinedValue.Instance;

            return false;
        }

        public void Source (ISetting setting, TextWriter output)
        {
            bool    comma = false;

            output.Write (setting.BlockBegin);
            output.Write ("define ");
            output.Write (this.name);
            output.Write ('(');

            foreach (NameExpression argument in this.arguments)
            {
                if (comma)
                    output.Write (", ");
                else
                    comma = true;

                output.Write (argument);
            }

            output.Write ("): ");

            this.body.Source (setting, output);

            output.Write (setting.BlockEnd);
        }

        #endregion
    }
}
