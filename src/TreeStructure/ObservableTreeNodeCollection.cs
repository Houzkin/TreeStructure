﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TreeStructure.EventManagement;
using TreeStructure.Linq;

namespace TreeStructure {
    /// <summary>観測可能な多分木構造をなすノードを表す</summary>
    /// <typeparam name="TNode">各ノードの共通基底クラスとなる型</typeparam>
    [Serializable]
    public class ObservableTreeNodeCollection<TNode> : TreeNodeCollection<TNode>, IObservableTreeNode<TNode>, INotifyPropertyChanged
        where TNode : ObservableTreeNodeCollection<TNode> {
        /// <summary>
        /// 新規インスタンスを初期化する
        /// </summary>
        public ObservableTreeNodeCollection() { this.PropertyChangeProxy = new PropertyChangeProxy(this); }
        /// <summary>新規インスタンスを初期化する</summary>
        /// <param name="collection">コレクション</param>
        public ObservableTreeNodeCollection(IEnumerable<TNode> collection) : this() {
            foreach (var item in collection) { this.AddChild(item); }
        }
        ReadOnlyObservableCollection<TNode>? _readonlyobservablecollection;
        /// <summary><inheritdoc/></summary>
        public override ReadOnlyObservableCollection<TNode> Children => _readonlyobservablecollection ??= new ReadOnlyObservableCollection<TNode>(ChildNodes);
        /// <summary><inheritdoc/></summary>
        protected override ObservableCollection<TNode> ChildNodes { get; } = new ObservableCollection<TNode>();

        /// <summary><inheritdoc/></summary>
        protected override Action<IEnumerable<TNode>, int, int> MoveAction => (collection, oldIdx, newIdx) =>
            ((ObservableCollection<TNode>)collection).Move(oldIdx, newIdx);


        IDisposable ShiftParentChangedNotification() {
            return UniqueExcutor.LateEvalute(parentchangedeventkey, () => Parent);
        }
        readonly string parentchangedeventkey = "in Library : " + nameof(ObservableTreeNodeCollection<TNode>)+ "." + nameof(Parent);
        readonly string disposedeventkey = "in Library : " + nameof(ObservableTreeNodeCollection<TNode>) + "." + nameof(Disposed);

        StructureChangedEventExecutor<TNode>? _uniqueExcutor;
        private StructureChangedEventExecutor<TNode> UniqueExcutor {
            get {
                if(_uniqueExcutor == null) {
                    _uniqueExcutor = new StructureChangedEventExecutor<TNode>(Self);
                    _uniqueExcutor.Register(disposedeventkey, () => Disposed?.Invoke(Self,EventArgs.Empty));
                    _uniqueExcutor.Register(parentchangedeventkey, () => RaisePropertyChanged(nameof(Parent)));
                }
                return _uniqueExcutor;
            }
        }
        PropertyChangeProxy PropertyChangeProxy;
        /// <summary>プロパティ変更通知を発行する</summary>
        /// <param name="propName"></param>
        protected void RaisePropertyChanged([CallerMemberName]string? propName = null) {
            PropertyChangeProxy.Notify(propName);
        }
        /// <summary>値の変更と変更通知の発行を行う</summary>
        protected virtual bool SetProperty<T>(ref T strage, T value, [CallerMemberName] string? propertyName = null) {
            return PropertyChangeProxy.SetWithNotify(ref strage, value, propertyName);
        }
        /// <summary><inheritdoc/></summary>
        public event PropertyChangedEventHandler? PropertyChanged {
            add { PropertyChangeProxy.Changed += value; }
            remove { PropertyChangeProxy.Changed -= value; }
        }
        /// <summary>ツリー構造が変化したとき発生する</summary>
        public event EventHandler<StructureChangedEventArgs<TNode>>? StructureChanged;
        void IObservableTreeNode<TNode>.OnStructureChanged(StructureChangedEventArgs<TNode> e) {
            StructureChanged?.Invoke(this, e);
        }
        /// <summary>破棄されたとき発生する</summary>
        public event EventHandler? Disposed;
        /// <summary><inheritdoc/></summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing) {
            IDisposable? dsp = null;
            if (disposing){
                if(Disposed != null) {
                    dsp = UniqueExcutor.ExecuteUnique(disposedeventkey);
                }
            }
            base.Dispose(disposing);
            dsp?.Dispose();
            Disposed = null;
        }
        /// <summary><inheritdoc/></summary>
        protected override void AddChildProcess(TNode child) {
            using (child.UniqueExcutor.LateEvaluateTree())
            using (child.ShiftParentChangedNotification()) {
                base.AddChildProcess(child);
            }
        }
        /// <summary><inheritdoc/></summary>
        protected override void InsertChildProcess(int index, TNode child) {
            using (child.UniqueExcutor.LateEvaluateTree())
            using (child.ShiftParentChangedNotification()) {
                base.InsertChildProcess(index, child);
            }
        }
        /// <summary><inheritdoc/></summary>
        protected override void RemoveChildProcess(TNode child) {
            using (child?.UniqueExcutor.LateEvaluateTree()) 
            using (child?.ShiftParentChangedNotification()) {
                base.RemoveChildProcess(child);
            }
        }
        /// <summary><inheritdoc/></summary>
        protected override void ClearChildProcess() {
            using (ChildNodes.Select(a => a?.UniqueExcutor.LateEvaluateTree()).OfType<IDisposable>().ToLumpDisposables()) 
            using (ChildNodes.Select(a => a?.ShiftParentChangedNotification()).OfType<IDisposable>().ToLumpDisposables()) {
                base.ClearChildProcess();
            }
        }
        /// <summary><inheritdoc/></summary>
        protected override void MoveChildProcess(int oldIndex, int newIndex) {
            using (ChildNodes.ElementAt(oldIndex)?.UniqueExcutor.LateEvaluateTree()){
                base.MoveChildProcess(oldIndex, newIndex);
            }
        }

    }
}
