﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TreeStructure.Collections;
using TreeStructure.Internals;
using TreeStructure.Utility;

namespace TreeStructure.EventManager {
    public class ChainedPropertyChangedEventArgs<T> : PropertyChangedEventArgs {
        [AllowNull]
        public T PropertyValue { get; init; }
        public ChainedPropertyChangedEventArgs(string? propertyName,[AllowNull] T propertyValue) : base(propertyName) {
            PropertyValue = propertyValue;
        }
    }
    public class ObservablePropertyTree<TSrc> {
        PropertyChainRoot _root;
        public PropertyChainNode Root => _root;
        public ObservablePropertyTree(TSrc target) {
            _root = new PropertyChainRoot(target);
        }
        public bool IsEvaluateTargetChanged {
            get => _root.IsEvaluateTargetChanged;
            set => _root.IsEvaluateTargetChanged = value;
        }
        public void ChangeTarget(TSrc target) {
            _root.ChangeTarget(target);
        }
        public NotifyObject<TValue> ToNotifyObject<TValue>(Expression<Func<TSrc, TValue>> expression) {
            return _root.ToNotifyObject(expression);
        }
        public IDisposable Subscribe(Expression<Func<TSrc, object>> expression, Action changedAction) {
            return Subscribe<object>(expression, (s, e) => changedAction());
        }
        public IDisposable Subscribe<TValue>(Expression<Func<TSrc, object>> expression, Action<TValue> changedAction) {
            return Subscribe<TValue>(expression, (s, e) => changedAction(e.PropertyValue));
        }
        public IDisposable Subscribe<TValue>(Expression<Func<TSrc, object>> expression, EventHandler<ChainedPropertyChangedEventArgs<TValue>> changedAction) {
            return _root.Subscribe(expression, changedAction);
        }

        public class PropertyChainNode : TreeNodeBase<PropertyChainNode> {
            #region Fields
            readonly HashSet<IEnumerable<string>> ChainToLeafs = new(EqualityCompared<IEnumerable<string>>.By(a => string.Join('.', a)));
            IDisposable? targetListener;
            #endregion

            #region Constructor
            internal PropertyChainNode(object? target) {
                this.NamedProperty = string.Empty;
                SubscribePropertyValue(target);
            }
            private PropertyChainNode(object? target,string named,IEnumerable<string> propNames) {
                NamedProperty = named;
                if(propNames.Any())
                    ChainToLeafs.Add(propNames);
                SubscribePropertyValue(target);
            }
            #endregion
            protected override ISet<PropertyChainNode> ChildNodes { get; } = new HashSet<PropertyChainNode>();
            protected override void AddChildProcess(PropertyChainNode child) {
                if(this.CanAddChildNode(child)) base.AddChildProcess(child);
            }
            protected override void RemoveChildProcess(PropertyChainNode child) {
                if (!ChildNodes.Contains(child)) return;
                base.RemoveChildProcess(child);
                foreach (var item in ChainToLeafs.Where(x => x.First() == child.NamedProperty).ToArray()){
                    var lst = new Stack<string>(item.Reverse());
                    foreach(var upstm in this.Upstream()) {
                        upstm.ChainToLeafs.Remove(lst);
                        lst.Push(upstm.NamedProperty);
                    }
                }
            }
            [AllowNull]
            public object Target { get; private set; }
            public string NamedProperty { get; init; }
            bool TrySetTarget(object? target) {
                if (ReferenceEquals(target,Target)) return false;
                Target = target;
                return true;
            }
            protected void AddSubscribeProperty(IEnumerable<string> propNames) {
                if (!propNames.Any()) return;
                if (!ChainToLeafs.Add(propNames)) return;
                var obsname = propNames.First();
                var cld = ChildNodes.FirstOrDefault(a => a.NamedProperty == obsname);
                if(cld != null) {
                    cld.AddSubscribeProperty(propNames.Skip(1));
                } else {
                    var propValue = GetValueFromPropertyName(Target, obsname);
                    this.AddChildProcess(new PropertyChainNode(propValue, obsname, propNames.Skip(1)));
                }
            }
            protected void SubscribePropertyValue(object? target) {
                if (!TrySetTarget(target)) return;//ターゲットが同一であれば何もしない
                targetListener?.Dispose();
                if(target is INotifyPropertyChanged self) {//ターゲットの観測を開始
                    targetListener = new EventListener<PropertyChangedEventHandler>(
                        h => self.PropertyChanged += h,
                        h => self.PropertyChanged -= h,
                        onPropChanged);
                } else { 
                    if(target!=null && ChainToLeafs.Any()) 
                        throw new InvalidCastException(
                            $"観測対象 : {this.Root().Target?.ToString()} において、PropertyChain : {string.Join('.',this.Upstream().Reverse().Skip(1))} の値は{nameof(INotifyPropertyChanged)}を実装している必要があります。"); 
                }
                foreach(var chain in ChainToLeafs) {
                    if (!chain.Any()) return;
                    var obsname = chain.First();
                    var propValue = GetValueFromPropertyName(Target, obsname);
                    var cld = ChildNodes.FirstOrDefault(a => a.NamedProperty == obsname);
                    if (cld != null) {
                        cld.SubscribePropertyValue(propValue);
                    } else {
                        this.AddChildProcess(new PropertyChainNode(propValue, obsname, chain.Skip(1)));
                    }
                }
            }
            void onPropChanged(object? sender, PropertyChangedEventArgs e) {
                if (string.IsNullOrEmpty(e.PropertyName)) return;
                //観測プロパティの変更だった場合、該当する子ノードの値を再設定
                var chgcld = this.ChildNodes.FirstOrDefault(a => a.NamedProperty == e.PropertyName);
                if (chgcld == null) return;
                chgcld.SubscribePropertyValue(GetValueFromPropertyName(Target, e.PropertyName));
                RaisePropertyChanged(chgcld.Leafs());
            }
            protected virtual void RaisePropertyChanged(IEnumerable<PropertyChainNode> leafs) {
                var root = this.Root();
                if(object.ReferenceEquals(root, this)) return;
                root.RaisePropertyChanged(leafs);
            }
            
            internal void Dispose() {
                targetListener?.Dispose();
                this.Parent?.RemoveChildProcess(Self);
            }
        }
        private class PropertyChainRoot : PropertyChainNode {
            HashSet<ChainStatus> chainStatuses = new(EqualityCompared<ChainStatus>.By(a => string.Join('.', a.Key.Prepend(a.PropertyValueType))));
            public PropertyChainRoot(TSrc target) : base(target) { }
            public bool IsEvaluateTargetChanged { get; set; } = true;
            public void ChangeTarget(TSrc target) {
                var pre = this.Target;
                this.SubscribePropertyValue(target);
                if (IsEvaluateTargetChanged && !ReferenceEquals(pre, this.Target)) {
                    this.RaisePropertyChanged(this.Leafs().Except(new[] { this }));
                }
            }
            
            public IDisposable Subscribe<TValue>(Expression<Func<TSrc, object>> expression, EventHandler<ChainedPropertyChangedEventArgs<TValue>> changedAction) {
                var propChain = GetPropertyPath(expression);
                var status = addChainStatus<TValue, object>(propChain);
                return getListener(status, changedAction);
            }
            /// <summary>指定したプロパティの値を読取専用プロパティ<see cref="NotificationObject{T}.Value"/>として表す、<see cref="INotifyPropertyChanged"/>実装オブジェクトを返す。</summary>
            /// <typeparam name="TValue"></typeparam>
            /// <param name="expression"></param>
            /// <returns></returns>
            public NotifyObject<TValue> ToNotifyObject<TValue>(Expression<Func<TSrc,TValue>> expression) {
                var propChain = GetPropertyPath(expression);
                return new NotifyObject<TValue>(propChain, addChainStatus<TValue,TValue>, getListener);
            }
            ChainStatus<TValue> addChainStatus<TValue, TProp>(IEnumerable<string> propChain) {
                this.AddSubscribeProperty(propChain);
                Func<TValue> getvalue = () => {
                    try {
                        if (this.Target != null) {
                            var val = this.DescendArrivals(a => a.NamedProperty, propChain.Prepend(string.Empty)).Single().Target;
                            if (val is TValue va) return va;
                        }
                        return default;
                    } catch (InvalidOperationException e) {
                        throw new InvalidOperationException("PropertyChainに重複がある、或いは登録されていないプロパティ値が指定されました。", e);
                    }
                };
                var propValue = getvalue();

                var status = chainStatuses.OfType<ChainStatus<TValue>>().FirstOrDefault(x => x.Key.SequenceEqual(propChain) && x.PropertyValueType == typeof(TValue).FullName);
                if (status == null) {
                    status = new ChainStatus<TValue>(propChain, getvalue);
                    chainStatuses.Add(status);
                }
                return status;
            }
            IDisposable getListener<T>(ChainStatus<T> status,EventHandler<ChainedPropertyChangedEventArgs<T>> changedAction) {
                var listener = new EventListener<EventHandler<ChainedPropertyChangedEventArgs<T>>>(
                    h => status.ChainedPropertyChanged += h,
                    h => status.ChainedPropertyChanged -= h,
                    changedAction);

                var dsp = new DisposableObject(() => {
                    listener.Dispose();
                    if (status.ChainedPropertyChanged.GetLength() == 0) {
                        chainStatuses.Remove(status);
                        TryRemoveNode(status.Key);
                    }
                });
                return dsp;
            }
            protected override void RaisePropertyChanged(IEnumerable<PropertyChainNode> leafs) {
                HashSet<ChainStatus> args = new();
                foreach (var leaf in leafs) {
                    var lfchain = leaf.Upstream().Reverse().Skip(1).Select(a => a.NamedProperty).ToArray();
                    foreach (var sts in chainStatuses) {
                        if (args.Contains(sts)) continue;
                        var mth = sts.Key.Zip(lfchain).TakeWhile((x, y) => x.First.Equals(x.Second));
                        if (mth.Any()) args.Add(sts);
                    }
                    if (args.Count == chainStatuses.Count) break;
                }
                foreach (var sts in args) {
                    sts.OnPropertyChanged(Target, sts);
                }
            }
            void TryRemoveNode(IEnumerable<string> seq) {
                var des = this.DescendTraces(a => a.NamedProperty, seq.Prepend(string.Empty)).FirstOrDefault()?.Skip(1)
                    ?? Enumerable.Empty<PropertyChainNode>();
                foreach (var sts in chainStatuses) {
                    var ext = this.DescendTraces(a => a.NamedProperty, sts.Key.Prepend(string.Empty)).FirstOrDefault()?.Skip(1)
                        ?? Enumerable.Empty<PropertyChainNode>();
                    des = des.Except(ext);
                }
                foreach (var rmv in des.ToArray()) rmv.Dispose();
            }
            
        }
        public class NotifyObject<T> :INotifyPropertyChanged,IDisposable{
            IEnumerable<string> propChain;
            Func<IEnumerable<string>, ChainStatus<T>> setFunc;
            Func<ChainStatus<T>, EventHandler<ChainedPropertyChangedEventArgs<T>>,IDisposable> getListener;
            List<Tuple<PropertyChangedEventHandler, IDisposable>> HandlerDispoPair = new();
            void Set(PropertyChangedEventHandler handler, IDisposable disp) { HandlerDispoPair.Add(Tuple.Create(handler, disp)); }
            void Remove(PropertyChangedEventHandler handler) {
                var tpl = HandlerDispoPair.FirstOrDefault(a=>a.Item1 == handler);
                if (tpl != null) {
                    tpl.Item2.Dispose();
                    HandlerDispoPair.Remove(tpl);
                }
            }
            internal NotifyObject(IEnumerable<string> propchain,Func<IEnumerable<string>,ChainStatus<T>> func ,Func<ChainStatus<T>,EventHandler<ChainedPropertyChangedEventArgs<T>>,IDisposable> toListener){
                propChain = propchain;
                setFunc = func;
                getListener = toListener;
                //初期値を設定するためにChainStatusを設定しDispose
                var sts = setFunc(propChain);
                this.Value = sts.BeforeValue;
                getListener(sts, (o, e) => { }).Dispose();
            }
            event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged {
                add { this.PropertyChanged += value; }
                remove { this.PropertyChanged -= value; }
            }
            public event PropertyChangedEventHandler PropertyChanged {
                add {
                    Set(value, getListener(setFunc(propChain), (o, e) => {
                        this.Value = e.PropertyValue;
                        value.Invoke(this, e);
                    }));
                }
                remove { Remove(value); }
            }
            public T Value { get; private set; }
            public void Dispose() {
                var hdlrs = HandlerDispoPair.Select(x=>x.Item1).ToArray();
                foreach (var hdlr in hdlrs) Remove(hdlr);

            }
        }

        internal abstract class ChainStatus {
            public abstract IEnumerable<string> Key { get; }
            public abstract string PropertyValueType { get; }
            public abstract void OnPropertyChanged(object? sender, ChainStatus propertyName);
        }
        internal class ChainStatus<TVal> : ChainStatus {
            public ChainStatus(IEnumerable<string> key,Func<TVal> getPresentValue) {
                this.Key = key;
                this.GetPresentValue = getPresentValue;
                this.BeforeValue = getPresentValue();
            }
            public override IEnumerable<string> Key { get; }
            public override string PropertyValueType => typeof(TVal).FullName ?? "";
            public TVal BeforeValue { get; private set; }
            public EventHandler<ChainedPropertyChangedEventArgs<TVal>>? ChainedPropertyChanged;
            public Func<TVal> GetPresentValue { get; private set; }

            public override void OnPropertyChanged(object? sender, ChainStatus status) {
                var curv = GetPresentValue();
                if(object.Equals(this.BeforeValue, curv)) return;
                var arg = new ChainedPropertyChangedEventArgs<TVal>(string.Join('.',status.Key), curv);
                ChainedPropertyChanged?.Invoke(sender, arg);
                BeforeValue = curv;
            }
        
        }

        #region static methods
        static IEnumerable<string> GetPropertyPath<T,TValue>(Expression<Func<T, TValue>> expression) {
            var memberExpression = GetMemberExpression(expression.Body);

            if (memberExpression == null) {
                throw new ArgumentException("Expression must be a member access expression");
            }
            var propNames = new Stack<string>();
            //var propertyNames = new List<string>();

            while (memberExpression != null) {
                //propertyNames.Insert(0, memberExpression.Member.Name);
                propNames.Push(memberExpression.Member.Name);
                memberExpression = GetMemberExpression(memberExpression.Expression);
            }

            return propNames;
        }

        static MemberExpression? GetMemberExpression([AllowNull]Expression expression) {
            if (expression is UnaryExpression unaryExpression) {
                return unaryExpression.Operand as MemberExpression;
            }
            return expression as MemberExpression;
        }

        static object? GetValueFromPropertyName(object? obj, string propertyName) {
            // obj が null の場合 null を返す
            if (obj == null) return null;
            // obj の型から指定されたプロパティを取得
            PropertyInfo? propertyInfo = obj.GetType().GetProperty(propertyName);

            // プロパティが存在するか確認
            if (propertyInfo != null) {
                // プロパティの値を取得
                return propertyInfo.GetValue(obj);
            } else {
                // プロパティが存在しない場合
                throw new ArgumentException($"Property '{propertyName}' not found in type {obj.GetType().Name}");
            }
        }
        #endregion
    }
    public class ObservablePropertyTree {
        public static ObservablePropertyTree<TSrc> Establish<TSrc>(TSrc target) {
            return new ObservablePropertyTree<TSrc>(target);
        }

    }
}
