using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MainForm : Form {
    private readonly OscRuntimeSession session;
    private readonly ToolStripButton connectButton;
    private readonly ToolStripTextBox oscEndPointField;
    private readonly TreeView stateTreeView;
    private readonly ContextMenuStrip treeContextMenu;
    private readonly ToolStripMenuItem editValueMenuItem;
    private readonly ToolStripMenuItem forceSyncMenuItem;
    private readonly ToolStripMenuItem deleteMenuItem;
    private readonly StatusStrip statusStrip;
    private readonly ToolStripStatusLabel statusTextLabel;
    private readonly HashSet<string> discoveredServiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TreeNode> selectedNodes = new HashSet<TreeNode>();
    private readonly Dictionary<TreeNode, string> editingOriginalText = new Dictionary<TreeNode, string>();
    private TreeNode? currentActiveNode;
    private Font? activeAvatarFont;
    private string lastValidOscEndPoint = "127.0.0.1:9001";
    private bool ignoreTreeEvents;
    private bool initialized;

    public MainForm() {
        Text = LocalizationManager.Get("App.Title");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(320, 640);
        ClientSize = new Size(450, 640);
        lastValidOscEndPoint = LoadOscEndPointSetting();

        session = new OscRuntimeSession("avistates.dat");
        connectButton = new ToolStripButton {
            Text = LocalizationManager.Get("MainForm.Button.Connect"),
            AutoSize = true,
            Anchor = AnchorStyles.Right,
        };
        connectButton.Click += OnConnectButtonClick;

        stateTreeView = new TreeView {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = true,
            ShowNodeToolTips = true,
            ImeMode = ImeMode.Disable,
            BorderStyle = BorderStyle.None,
            LabelEdit = true,
        };
        stateTreeView.AfterCheck += OnStateTreeAfterCheck;
        stateTreeView.NodeMouseClick += OnStateTreeNodeMouseClick;
        stateTreeView.NodeMouseDoubleClick += OnStateTreeNodeMouseDoubleClick;
        stateTreeView.KeyDown += OnStateTreeKeyDown;
        stateTreeView.AfterSelect += OnStateTreeAfterSelect;
        stateTreeView.BeforeLabelEdit += OnStateTreeBeforeLabelEdit;
        stateTreeView.AfterLabelEdit += OnStateTreeAfterLabelEdit;

        editValueMenuItem = new ToolStripMenuItem(LocalizationManager.Get("MainForm.Menu.EditValue")) {
            ShortcutKeys = Keys.F2,
        };
        editValueMenuItem.Click += OnEditValueMenuItemClick;
        forceSyncMenuItem = new ToolStripMenuItem(LocalizationManager.Get("MainForm.Menu.ForceSyncOnce"));
        forceSyncMenuItem.Click += OnForceSyncMenuItemClick;
        deleteMenuItem = new ToolStripMenuItem(LocalizationManager.Get("MainForm.Menu.RemoveFromDatabase")) {
            ShortcutKeys = Keys.Delete,
        };
        deleteMenuItem.Click += OnDeleteMenuItemClick;
        treeContextMenu = new ContextMenuStrip();
        var items = treeContextMenu.Items;
        items.Add(editValueMenuItem);
        items.Add(new ToolStripSeparator());
        items.Add(forceSyncMenuItem);
        items.Add(new ToolStripSeparator());
        items.Add(deleteMenuItem);
        treeContextMenu.Opening += OnTreeContextMenuOpening;
        stateTreeView.ContextMenuStrip = treeContextMenu;

        statusTextLabel = new ToolStripStatusLabel {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            BorderSides = ToolStripStatusLabelBorderSides.None,
            Margin = new Padding(2, 2, 2, 2),
        };
        oscEndPointField = new ToolStripTextBox {
            Text = lastValidOscEndPoint,
            AutoSize = false,
            Width = 150,
        };
        oscEndPointField.TextChanged += OnOscEndPointFieldTextChanged;
        statusStrip = new StatusStrip {
            SizingGrip = true,
            RenderMode = ToolStripRenderMode.System,
        };
        var statusItems = statusStrip.Items;
        statusItems.Add(statusTextLabel);
        statusItems.Add(new ToolStripSeparator());
        statusItems.Add(oscEndPointField);
        statusItems.Add(new ToolStripSeparator());
        statusItems.Add(connectButton);

        session.StateChanged += OnSessionStateChanged;
        session.RemoteServiceDiscovered += OnRemoteServiceDiscovered;
        session.RemoteServiceLost += OnRemoteServiceLost;
        session.IncomingOscObserved += OnIncomingOscObserved;

        var controls = Controls;
        controls.Add(stateTreeView);
        controls.Add(statusStrip);

        Load += OnFormLoad;
        FormClosed += OnFormClosed;
    }
    private async void OnFormLoad(object? sender, EventArgs e) {
        if (initialized) return;

        initialized = true;
        try {
            SetStatus(LocalizationManager.Get("MainForm.Status.InitializingDatabase"));
            await session.InitializeAsync(CancellationToken.None);
            RequestTreeReload();
            SetStatus(LocalizationManager.Get("MainForm.Status.Ready"));
        } catch (Exception ex) {
            ShowError(LocalizationManager.Get("MainForm.Error.InitializeDatabase"), ex);
            SetStatus(LocalizationManager.Get("MainForm.Status.InitializationFailed"));
        }
    }

    private async void OnConnectButtonClick(object? sender, EventArgs e) {
        connectButton.Enabled = false;
        try {
            if (session.IsConnected) {
                SetStatus(LocalizationManager.Get("MainForm.Status.Disconnecting"));
                await session.DisconnectAsync();
                connectButton.Text = LocalizationManager.Get("MainForm.Button.Connect");
                oscEndPointField.Enabled = true;
                discoveredServiceIds.Clear();
                SetStatus(LocalizationManager.Get("MainForm.Status.Disconnected"));
                return;
            }

            // Extract and validate IP:port from field
            if (!TryParseOscEndPoint(oscEndPointField.Text, out var oscIP, out int oscPort)) {
                ShowError(LocalizationManager.Get("MainForm.Error.InvalidOscEndPoint"), new InvalidOperationException("Invalid IP:port format. Expected format: IP:port"));
                SetStatus(LocalizationManager.Get("MainForm.Status.ConnectionError"));
                connectButton.Enabled = true;
                return;
            }

            discoveredServiceIds.Clear();
            SetStatus(LocalizationManager.Get("MainForm.Status.Connecting"));
            await session.ConnectAsync(oscIP, oscPort, CancellationToken.None);
            connectButton.Text = LocalizationManager.Get("MainForm.Button.Disconnect");
            oscEndPointField.Enabled = false;
            SetStatus(LocalizationManager.Get("MainForm.Status.ConnectedListening"));
            UpdateRemoteServiceStatus();
        } catch (Exception ex) {
            ShowError(LocalizationManager.Get("MainForm.Error.ChangeConnectionState"), ex);
            SetStatus(LocalizationManager.Get("MainForm.Status.ConnectionError"));
        } finally {
            connectButton.Enabled = true;
        }
    }

    private void UpdateConnectionStatusText() => SetStatus(session.IsConnected ?
        LocalizationManager.Get("MainForm.Status.Connected") :
        LocalizationManager.Get("MainForm.Status.Disconnected")
    );

    private void OnSessionStateChanged(object? sender, StateChangeBatchEventArgs e) {
        if (IsHandleCreated && InvokeRequired) {
            BeginInvoke(new Action<StateChangeBatchEventArgs>(ProcessStateChanges), e);
        } else {
            ProcessStateChanges(e);
        }
    }

    private void ProcessStateChanges(StateChangeBatchEventArgs eventArgs) {
        if (stateTreeView.IsDisposed) return;

        try {
            ignoreTreeEvents = true;
            stateTreeView.BeginUpdate();

            bool hasActiveAvatar = session.TryGetActiveAvatarId(out var activeAvatarId);
            var changes = eventArgs.changes;

            // Pre-build lookup structures for efficient processing
            var avatarIdToNode = new Dictionary<Guid, TreeNode>();
            var paramKeyToNode = new Dictionary<AvatarParameterKey, TreeNode>();
            foreach (TreeNode node in stateTreeView.Nodes)
                if (node.Tag is TreeNodeTag tag && tag.isAvatarNode) {
                    avatarIdToNode[tag.entry.avatarId] = node;
                    foreach (TreeNode child in node.Nodes)
                        if (child.Tag is TreeNodeTag childTag &&
                            !childTag.isAvatarNode &&
                            childTag.entry.parameterName != null)
                            paramKeyToNode[childTag.entry.GetKey()] = child;
                }

            // Process each change notification
            for (int i = 0, count = changes.Count; i < count; i++) {
                var change = changes[i];
                switch (change.changeType) {
                    case StateChangeType.ParameterChanged:
                        ProcessParameterChanged(change, hasActiveAvatar, activeAvatarId, avatarIdToNode, paramKeyToNode);
                        break;
                    case StateChangeType.ParameterRemoved:
                        ProcessParameterRemoved(change, avatarIdToNode, paramKeyToNode);
                        break;
                    case StateChangeType.AvatarRemoved:
                        ProcessAvatarRemoved(change, avatarIdToNode);
                        break;
                }
            }

            // Update avatar node checked states (check if any child is enabled)
            var avatarCheckedStates = new Dictionary<Guid, bool>();
            foreach (var avatarNode in avatarIdToNode.Values) {
                if (avatarNode.Tag is TreeNodeTag tag) {
                    bool hasAnyChecked = false;
                    foreach (TreeNode child in avatarNode.Nodes) {
                        if (child.Checked) {
                            hasAnyChecked = true;
                            break;
                        }
                    }
                    avatarNode.Checked = hasAnyChecked;
                }
            }
        } catch (Exception ex) {
            ShowError(LocalizationManager.Get("MainForm.Error.ProcessStateChanges"), ex);
        } finally {
            stateTreeView.EndUpdate();
            ignoreTreeEvents = false;
        }
    }

    private void ProcessParameterChanged(
        StateChangeNotification change,
        bool hasActiveAvatar,
        Guid activeAvatarId,
        Dictionary<Guid, TreeNode> avatarIdToNode,
        Dictionary<AvatarParameterKey, TreeNode> paramKeyToNode
    ) {
        var key = change.GetParameterKey();

        // Ensure avatar node exists
        if (!avatarIdToNode.TryGetValue(change.avatarId, out var avatarNode)) {
            var avatarIdString = change.avatarId.ToString("D");
            avatarNode = new TreeNode(avatarIdString) {
                Tag = new TreeNodeTag(change.avatarId),
                ToolTipText = avatarIdString,
            };
            stateTreeView.Nodes.Add(avatarNode);
            avatarIdToNode[change.avatarId] = avatarNode;
        }

        if (hasActiveAvatar && change.avatarId.Equals(activeAvatarId)) {
            activeAvatarFont ??= new Font(stateTreeView.Font, FontStyle.Bold);
            avatarNode.NodeFont = activeAvatarFont;
            if (currentActiveNode != null && avatarNode != currentActiveNode)
                currentActiveNode.NodeFont = stateTreeView.Font;
            currentActiveNode = avatarNode;
        } else
            avatarNode.NodeFont = stateTreeView.Font;

        // Update or create parameter node
        if (!paramKeyToNode.TryGetValue(key, out var paramNode)) {
            // Create new parameter node
            var tempEntry = change.ToStateEntry(DateTimeOffset.UtcNow);
            paramNode = new TreeNode {
                Tag = new TreeNodeTag(tempEntry),
            };
            avatarNode.Nodes.Add(paramNode);
            paramKeyToNode[key] = paramNode;
        }

        // Update parameter node properties
        var tag = paramNode.Tag as TreeNodeTag;
        var updatedEntry = change.ToStateEntry(DateTimeOffset.UtcNow);
        paramNode.Tag = new TreeNodeTag(updatedEntry);
        paramNode.Text = BuildParameterText(updatedEntry);
        paramNode.ToolTipText = change.newValue.ToString();
        paramNode.Checked = change.syncEnabled;
    }

    private void ProcessParameterRemoved(
        StateChangeNotification change,
        Dictionary<Guid, TreeNode> avatarIdToNode,
        Dictionary<AvatarParameterKey, TreeNode> paramKeyToNode
    ) {
        var key = change.GetParameterKey();

        if (!paramKeyToNode.TryGetValue(key, out var paramNode)) return;
        paramNode.Parent?.Nodes.Remove(paramNode);
        paramKeyToNode.Remove(key);

        // Clean up avatar node if it has no more children
        if (!(paramNode.Parent is TreeNode avatarNode) || avatarNode.Nodes.Count != 0) return;
        stateTreeView.Nodes.Remove(avatarNode);
        avatarIdToNode.Remove(change.avatarId);
    }

    private void ProcessAvatarRemoved(StateChangeNotification change, Dictionary<Guid, TreeNode> avatarIdToNode) {
        if (!avatarIdToNode.TryGetValue(change.avatarId, out var avatarNode)) return;
        stateTreeView.Nodes.Remove(avatarNode);
        avatarIdToNode.Remove(change.avatarId);
    }

    private void RequestTreeReload() {
        if (!IsHandleCreated || IsDisposed) return;
        PopulateTreeFromDatabase();
    }

    private void PopulateTreeFromDatabase() {
        if (stateTreeView.IsDisposed) return;

        var states = session.GetAllStates();
        bool hasActiveAvatar = session.TryGetActiveAvatarId(out var activeAvatarId);

        ignoreTreeEvents = true;
        try {
            stateTreeView.BeginUpdate();
            stateTreeView.Nodes.Clear();

            var avatarNodes = new Dictionary<Guid, TreeNode>();
            var avatarHasEnabled = new Dictionary<Guid, bool>();

            for (int index = 0, stateCount = states.Count; index < stateCount; index++) {
                var state = states[index];
                if (!avatarNodes.TryGetValue(state.avatarId, out var avatarNode)) {
                    var avatarIdString = state.avatarId.ToString("D");
                    avatarNode = new TreeNode(avatarIdString) {
                        Tag = new TreeNodeTag(state.avatarId),
                        ToolTipText = avatarIdString,
                    };
                    avatarNodes.Add(state.avatarId, avatarNode);
                    avatarHasEnabled[state.avatarId] = false;
                    stateTreeView.Nodes.Add(avatarNode);
                }

                avatarNode.NodeFont = hasActiveAvatar && state.avatarId.Equals(activeAvatarId)
                    ? (activeAvatarFont ??= new Font(stateTreeView.Font, FontStyle.Bold))
                    : stateTreeView.Font;

                var paramNode = new TreeNode {
                    Tag = new TreeNodeTag(state),
                };
                avatarNode.Nodes.Add(paramNode);
                paramNode.Text = BuildParameterText(state);
                paramNode.ToolTipText = state.value.ToString();
                paramNode.Checked = state.syncEnabled;

                if (state.syncEnabled) avatarHasEnabled[state.avatarId] = true;
            }

            foreach (var kv in avatarNodes)
                if (avatarHasEnabled.TryGetValue(kv.Key, out bool checkedState))
                    kv.Value.Checked = checkedState;
        } finally {
            stateTreeView.EndUpdate();
            ignoreTreeEvents = false;
        }
    }

    private static string BuildParameterText(AvatarParameterStateEntry state) {
        string text = $"{state.parameterName} = {state.value}";
        if (!state.syncEnabled)
            text += LocalizationManager.Get("MainForm.Parameter.DisabledSuffix");
        return text;
    }

    private async void OnStateTreeAfterCheck(object? sender, TreeViewEventArgs e) {
        if (ignoreTreeEvents || e.Action == TreeViewAction.Unknown)
            return;
        var node = e.Node;
        if (node == null || !(node.Tag is TreeNodeTag tag))
            return;
        try {
            ignoreTreeEvents = true;
            if (tag.isAvatarNode)
                await ApplyAvatarSyncStateAsync(tag.entry.avatarId, node.Checked);
            else if (tag.entry.parameterName != null)
                await session.SetSynchronizationEnabledAsync(tag.entry.GetKey(), node.Checked, CancellationToken.None);

            if (session.IsConnected) _ = session.SendEnabledParametersBackForAvatarIfActiveAsync(tag.entry.avatarId);

        } catch (Exception ex) {
            ShowError(LocalizationManager.Get("MainForm.Error.UpdateSyncState"), ex);
        } finally {
            ignoreTreeEvents = false;
        }
    }

    private async Task ApplyAvatarSyncStateAsync(Guid avatarId, bool enabled) {
        var avatarNode = FindAvatarNode(avatarId);
        if (avatarNode == null)
            return;

        var childNodes = avatarNode.Nodes;
        int childCount = childNodes.Count;
        for (int index = 0; index < childCount; index++)
            if (childNodes[index].Tag is TreeNodeTag tag && tag.entry.parameterName != null)
                await session.SetSynchronizationEnabledAsync(tag.entry.GetKey(), enabled, CancellationToken.None);
    }

    private TreeNode? FindAvatarNode(Guid avatarId) {
        var treeNodes = stateTreeView.Nodes;
        for (int nodeIndex = 0, nodeCount = treeNodes.Count; nodeIndex < nodeCount; nodeIndex++) {
            var candidate = treeNodes[nodeIndex];
            if (candidate.Tag is TreeNodeTag tag && tag.isAvatarNode && tag.entry.avatarId.Equals(avatarId))
                return candidate;
        }
        return null;
    }

    private void OnStateTreeNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e) {
        if (e.Button == MouseButtons.Right) {
            if (!selectedNodes.Contains(e.Node)) {
                SelectSingleNode(e.Node);
            }
            stateTreeView.SelectedNode = e.Node;
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        if ((ModifierKeys & Keys.Control) == Keys.Control)
            ToggleNodeSelection(e.Node);
        else
            SelectSingleNode(e.Node);

        stateTreeView.SelectedNode = e.Node;
    }

    private async void OnStateTreeNodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e) {
        if (!session.IsConnected) {
            SetStatus(LocalizationManager.Get("MainForm.Status.SyncSuspended"));
            return;
        }

        var node = e.Node;
        if (node == null || !(node.Tag is TreeNodeTag tag))
            return;

        await ForceSyncTagAsync(tag);
    }

    private void OnStateTreeBeforeLabelEdit(object? sender, NodeLabelEditEventArgs e) {
        var node = e.Node;
        if (node == null ||
            !(node.Tag is TreeNodeTag tag) ||
            tag.isAvatarNode ||
            tag.entry.parameterName == null ||
            !tag.entry.value.IsValid) {
            e.CancelEdit = true;
            return;
        }

        SetStatus(LocalizationManager.Get("MainForm.Status.EditingHint"));
        editingOriginalText[node] = node.Text;
        node.Text = tag.entry.value.ToString();
    }

    private async void OnStateTreeAfterLabelEdit(object? sender, NodeLabelEditEventArgs e) {
        var node = e.Node;
        if (node == null ||
            !editingOriginalText.TryGetValue(node, out var originalText) ||
            originalText == null)
            return;

        editingOriginalText.Remove(node);

        if (!(node.Tag is TreeNodeTag tag) ||
            tag.isAvatarNode ||
            tag.entry.parameterName == null ||
            e.Label == null) {
            node.Text = originalText;
            UpdateConnectionStatusText();
            return;
        }

        string editedText = e.Label.Trim();
        if (editedText.Length == 0) {
            e.CancelEdit = true;
            node.Text = originalText;
            UpdateConnectionStatusText();
            return;
        }

        if (!TryParseEditedValue(tag, editedText, out Primitive32 parsedValue)) {
            e.CancelEdit = true;
            node.Text = originalText;
            ShowError(LocalizationManager.Get("MainForm.Error.InvalidValue"), new InvalidOperationException(LocalizationManager.Get("MainForm.Error.InvalidValueType")));
            UpdateConnectionStatusText();
            return;
        }

        try {
            bool updated = await session.UpdateParameterValueAsync(tag.entry.GetKey(), parsedValue, CancellationToken.None);
            if (!updated) {
                e.CancelEdit = true;
                node.Text = originalText;
                UpdateConnectionStatusText();
                return;
            }

            node.Text = BuildParameterText(tag.entry);
            node.ToolTipText = parsedValue.ToString();
            tag.SetValue(parsedValue);
            UpdateConnectionStatusText();
        } catch (Exception ex) {
            e.CancelEdit = true;
            node.Text = originalText;
            ShowError(LocalizationManager.Get("MainForm.Error.UpdateParameterValue"), ex);
            UpdateConnectionStatusText();
        }
    }

    private void OnStateTreeAfterSelect(object? sender, TreeViewEventArgs e) {
        if (ignoreTreeEvents) return;
        var node = e.Node;
        if (node != null && (ModifierKeys & Keys.Control) != Keys.Control) SelectSingleNode(node);
    }

    private void OnStateTreeKeyDown(object? sender, KeyEventArgs e) {
        if (e.KeyCode == Keys.F2) {
            e.Handled = true;
            e.SuppressKeyPress = true;
            BeginEditSelectedParameter();
            return;
        }

        if (e.KeyCode != Keys.Delete) return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        OnDeleteMenuItemClick(sender, EventArgs.Empty);
    }

    private void OnTreeContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) {
        var selection = GetSelectionForDelete();
        int selectionCount = selection.Count;
        editValueMenuItem.Enabled = CanEditSingleSelectedParameter();
        forceSyncMenuItem.Enabled = session.IsConnected && selectionCount > 0;
        deleteMenuItem.Enabled = selectionCount > 0;
    }

    private void OnEditValueMenuItemClick(object? sender, EventArgs e) => BeginEditSelectedParameter();

    private async void OnForceSyncMenuItemClick(object? sender, EventArgs e) {
        if (!session.IsConnected) {
            SetStatus(LocalizationManager.Get("MainForm.Status.SyncSuspended"));
            return;
        }

        var selection = GetSelectionForDelete();
        int selectionCount = selection.Count;
        if (selectionCount == 0) return;

        int syncedCount = 0;
        for (int index = 0; index < selectionCount; index++)
            if (await ForceSyncTagAsync(selection[index]))
                syncedCount++;

        if (syncedCount > 0)
            SetStatus(LocalizationManager.GetWithInt(syncedCount == 1 ? "MainForm.Status.ForceSyncedOne" : "MainForm.Status.ForceSyncedMany", syncedCount));
        else
            SetStatus(LocalizationManager.Get("MainForm.Status.ForceSyncedNone"));
    }

    private async void OnDeleteMenuItemClick(object? sender, EventArgs e) {
        List<TreeNodeTag> selection = GetSelectionForDelete();
        int selectionCount = selection.Count;
        if (selectionCount == 0) return;

        int avatarCount = 0;
        int parameterCount = 0;
        for (int index = 0; index < selectionCount; index++) {
            if (selection[index].isAvatarNode) {
                avatarCount++;
            } else {
                parameterCount++;
            }
        }

        if (MessageBox.Show(
            this,
            avatarCount + parameterCount > 1 ?
                LocalizationManager.Get("MainForm.Confirm.DeleteSelected") :
            avatarCount == 1 ?
                LocalizationManager.Get("MainForm.Confirm.DeleteAvatar") :
                LocalizationManager.Get("MainForm.Confirm.DeleteParameter"),
            LocalizationManager.Get("MainForm.Confirm.Title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning
        ) != DialogResult.Yes) return;

        try {
            var avatarsToDelete = new HashSet<Guid>();
            var parametersToDelete = new HashSet<AvatarParameterKey>();
            var parameterDeletes = new List<TreeNodeTag>();

            for (int index = 0; index < selectionCount; index++) {
                var tag = selection[index];
                if (tag.isAvatarNode) avatarsToDelete.Add(tag.entry.avatarId);
            }

            for (int index = 0; index < selectionCount; index++) {
                var tag = selection[index];
                if (!tag.isAvatarNode &&
                    tag.entry.parameterName != null &&
                    !avatarsToDelete.Contains(tag.entry.avatarId) &&
                    parametersToDelete.Add(tag.entry.GetKey()))
                    parameterDeletes.Add(tag);
            }

            foreach (var avatarId in avatarsToDelete)
                await session.DeleteAvatarStatesAsync(avatarId, CancellationToken.None);

            int parameterDeleteCount = parameterDeletes.Count;
            for (int index = 0; index < parameterDeleteCount; index++)
                await session.DeleteParameterStateAsync(
                    parameterDeletes[index].entry.GetKey(),
                    CancellationToken.None
                );

            ClearMultiSelection();
        } catch (Exception ex) {
            ShowError(LocalizationManager.Get("MainForm.Error.DeleteSelection"), ex);
        }
    }

    private void OnRemoteServiceDiscovered(OscRemoteServiceInfo serviceInfo) {
        if (IsHandleCreated && InvokeRequired) {
            BeginInvoke(new Action<OscRemoteServiceInfo>(OnRemoteServiceDiscovered), serviceInfo);
            return;
        }

        discoveredServiceIds.Add(serviceInfo.id);
        UpdateRemoteServiceStatus();
    }

    private void OnRemoteServiceLost(OscRemoteServiceInfo serviceInfo) {
        if (IsHandleCreated && InvokeRequired) {
            BeginInvoke(new Action<OscRemoteServiceInfo>(OnRemoteServiceLost), serviceInfo);
            return;
        }

        discoveredServiceIds.Remove(serviceInfo.id);
        UpdateRemoteServiceStatus();
        if (currentActiveNode != null) {
            currentActiveNode.NodeFont = stateTreeView.Font;
            currentActiveNode = null;
        }
    }

    private async void OnIncomingOscObserved() {
        await Task.Yield();
        if (IsHandleCreated && InvokeRequired) {
            BeginInvoke(new Action(OnIncomingOscObserved));
            return;
        }

        UpdateRemoteServiceStatus();
    }

    private void UpdateRemoteServiceStatus() {
        if (!session.IsConnected) return;

        if (session.HasSeenIncomingOsc) {
            if (session.TryGetActiveAvatarId(out _))
                SetStatus(LocalizationManager.Get("MainForm.Status.ReceivingOsc"));
            else
                SetStatus(LocalizationManager.Get("MainForm.Status.ReceivingOscWaitingAvatar"));
            return;
        }

        if (discoveredServiceIds.Count == 0)
            SetStatus($"{LocalizationManager.Get("MainForm.Status.Connected")} - {LocalizationManager.Get("MainForm.Status.WaitingForServices")}");
        else
            SetStatus(LocalizationManager.Get("MainForm.Status.NoOscTrafficHint"));
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e) {
        session.StateChanged -= OnSessionStateChanged;
        session.RemoteServiceDiscovered -= OnRemoteServiceDiscovered;
        session.RemoteServiceLost -= OnRemoteServiceLost;
        session.IncomingOscObserved -= OnIncomingOscObserved;
        activeAvatarFont?.Dispose();
#pragma warning disable CA2012
        _ = session.DisposeAsync();
#pragma warning restore CA2012
    }

    private void SetStatus(string text) {
        if (IsHandleCreated && InvokeRequired) {
            BeginInvoke(new Action<string>(SetStatus), text);
            return;
        }

        statusTextLabel.Text = text;
    }

    private void ShowError(string message, Exception exception) {
        if (IsHandleCreated && InvokeRequired) {
            BeginInvoke(new Action<string, Exception>(ShowError), message, exception);
            return;
        }
        MessageBox.Show(
            this,
            $"{message}\n\n{exception.Message}\n{exception.StackTrace}",
            LocalizationManager.Get("App.Title"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error
        );
    }

    private List<TreeNodeTag> GetSelectionForDelete() {
        var tags = new List<TreeNodeTag>();
        var keys = new HashSet<AvatarParameterKey>();
        int selectedCount = selectedNodes.Count;
        if (selectedCount > 0) {
            foreach (var node in selectedNodes)
                if (node.Tag is TreeNodeTag tag && keys.Add(tag.entry.GetKey()))
                    tags.Add(tag);
            return tags;
        }
        var selectedNode = stateTreeView.SelectedNode;
        if (selectedNode != null && selectedNode.Tag is TreeNodeTag selectedTag)
            tags.Add(selectedTag);
        return tags;
    }

    private void SelectSingleNode(TreeNode node) {
        ClearMultiSelection();
        selectedNodes.Add(node);
        ApplyNodeSelectionVisual(node, true);
    }

    private void ToggleNodeSelection(TreeNode node) {
        if (selectedNodes.Remove(node)) {
            ApplyNodeSelectionVisual(node, false);
            return;
        }
        selectedNodes.Add(node);
        ApplyNodeSelectionVisual(node, true);
    }

    private void ClearMultiSelection() {
        foreach (var node in selectedNodes)
            ApplyNodeSelectionVisual(node, false);
        selectedNodes.Clear();
    }

    private void ApplyNodeSelectionVisual(TreeNode node, bool selected) {
        if (selected) {
            node.BackColor = SystemColors.Highlight;
            node.ForeColor = SystemColors.HighlightText;
            return;
        }
        node.BackColor = stateTreeView.BackColor;
        node.ForeColor = stateTreeView.ForeColor;
    }

    private Task<bool> ForceSyncTagAsync(TreeNodeTag tag) {
        if (tag.isAvatarNode) return session.ForceSyncAvatarIfActiveAsync(tag.entry.avatarId);
        var parameterName = tag.entry.parameterName;
        return parameterName != null ? session.ForceSyncParameterAsync(tag.entry.avatarId, parameterName, CancellationToken.None) : Task.FromResult(false);
    }

    private void BeginEditSelectedParameter() {
        if (CanEditSingleSelectedParameter()) stateTreeView.SelectedNode?.BeginEdit();
    }

    private bool CanEditSingleSelectedParameter() {
        if (selectedNodes.Count > 1) return false;
        var node = stateTreeView.SelectedNode;
        return node != null && node.Tag is TreeNodeTag tag && !tag.isAvatarNode && tag.entry.parameterName != null;
    }

    private static bool TryParseEditedValue(TreeNodeTag tag, string editedText, out Primitive32 value) {
        value = Primitive32.Null;
        switch (tag.entry.value.GetTypeCode()) {
            case TypeCode.Boolean:
                if (bool.TryParse(editedText, out bool boolValue)) {
                    value = boolValue;
                    return true;
                }
                if (int.TryParse(editedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int boolInt) && (boolInt == 0 || boolInt == 1)) {
                    value = boolInt != 0;
                    return true;
                }
                break;
            case TypeCode.Single:
                if (float.TryParse(editedText, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue)) {
                    value = floatValue;
                    return true;
                }
                break;
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
                if (int.TryParse(editedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) {
                    value = intValue;
                    return true;
                }
                break;
        }
        return false;
    }

    private void OnOscEndPointFieldTextChanged(object? sender, EventArgs e) {
        string text = oscEndPointField.Text.Trim();
        if (string.IsNullOrEmpty(text)) {
            oscEndPointField.ForeColor = SystemColors.ControlText;
            return;
        }
        if (TryParseOscEndPoint(text, out _, out _)) {
            oscEndPointField.ForeColor = SystemColors.ControlText;
            lastValidOscEndPoint = text;
            SaveOscEndPointSetting(text);
            return;
        }
        oscEndPointField.ForeColor = Color.Red;
    }

    private static bool TryParseOscEndPoint(string text, out IPAddress? oscIP, out int oscPort) {
        oscIP = null;
        oscPort = 9001;
        var textSpan = text.AsSpan().Trim();
        int colonIndex = textSpan.LastIndexOf(':');
        return colonIndex > 0 && IPAddress.TryParse(textSpan.Slice(0, colonIndex), out oscIP) &&
            int.TryParse(textSpan.Slice(colonIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out oscPort) &&
            oscPort >= 1 && oscPort <= 65535;
    }

    private static string LoadOscEndPointSetting() {
        try {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            string? value = configFile.AppSettings.Settings["OscEndPoint"]?.Value;
            return value ?? "127.0.0.1:9001";
        } catch {
            return "127.0.0.1:9001";
        }
    }

    private static void SaveOscEndPointSetting(string endPoint) {
        try {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var appSettings = configFile.AppSettings.Settings;
            var oscEndPoint = appSettings["OscEndPoint"];
            if (oscEndPoint != null)
                oscEndPoint.Value = endPoint;
            else
                appSettings.Add("OscEndPoint", endPoint);
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        } catch {
            // Silently ignore save failures
        }
    }

    private sealed class TreeNodeTag {
        public AvatarParameterStateEntry entry;
        public readonly bool isAvatarNode;
        public bool syncEnabled;

        public void SetValue(Primitive32 newValue) {
            entry = new AvatarParameterStateEntry(
                entry.avatarId,
                entry.parameterName,
                newValue,
                entry.syncEnabled,
                DateTimeOffset.UtcNow
            );
        }

        public TreeNodeTag(AvatarParameterStateEntry entry) {
            this.entry = entry;
        }

        public TreeNodeTag(Guid avatarId) {
            entry = new AvatarParameterStateEntry(avatarId, null, Primitive32.Null, false, DateTimeOffset.UtcNow);
            isAvatarNode = true;
        }
    }
}

