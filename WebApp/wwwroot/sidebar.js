async function getJSON(url) {
    const resp = await fetch(url);
    if (!resp.ok) {
        alert('Could not load tree data. See console for more details.');
        console.error(await resp.text());
        return [];
    }
    return resp.json();
}

function createTreeNode(id, text, icon, children = false) {
    return { id, text, children, itree: { icon } };
}

async function getHubs() {
    const hubs = await getJSON('/api/hubs');
    return hubs.map(hub => createTreeNode(`hub|${hub.id}`, hub.name, 'icon-hub', true));
}

async function getProjects(hubId) {
    const projects = await getJSON(`/api/hubs/${hubId}/projects`);
    return projects.map(project => createTreeNode(`project|${hubId}|${project.id}`, project.name, 'icon-project', true));
}

// Folders, Revit files and exchanges (the server leaves out the other files)
async function getContents(hubId, projectId, folderId = null) {
    const contents = await getJSON(`/api/hubs/${hubId}/projects/${projectId}/contents` + (folderId ? `?folder_id=${folderId}` : ''));
    return contents.map(item => {
        if (item.folder) {
            return createTreeNode(`folder|${hubId}|${projectId}|${item.id}`, item.name, 'icon-my-folder', true);
        }
        return createTreeNode(`${item.kind}|${hubId}|${projectId}|${item.id}`, item.name, item.kind === 'exchange' ? 'icon-exchange' : 'icon-revit');
    });
}

// onSelectionChanged({ kind: 'revit' | 'exchange', hubId, projectId, itemId, name }): a Revit file or an exchange was clicked
export function initTree(selector, onSelectionChanged) {
    // See http://inspire-tree.com
    const tree = new InspireTree({
        data: function (node) {
            if (!node || !node.id) {
                return getHubs();
            }
            const tokens = node.id.split('|');
            switch (tokens[0]) {
                case 'hub': return getProjects(tokens[1]);
                case 'project': return getContents(tokens[1], tokens[2]);
                case 'folder': return getContents(tokens[1], tokens[2], tokens[3]);
                default: return [];
            }
        }
    });
    tree.on('node.click', function (event, node) {
        event.preventTreeDefault();
        const [kind, hubId, projectId, itemId] = node.id.split('|');
        if (kind === 'revit' || kind === 'exchange') {
            node.select();
            onSelectionChanged({ kind, hubId, projectId, itemId, name: node.text });
        } else {
            node.toggleCollapse();
        }
    });
    new InspireTreeDOM(tree, { target: selector });

    // Refreshing reloads the whole tree, then expands again the nodes that were expanded (each expansion loads
    // the node's children, which triggers the expansion of the next level) and selects the selected node again
    const restore = { expanded: new Set(), selected: null };
    const restoreState = (nodes) => {
        nodes.forEach(node => {
            if (node.id === restore.selected) {
                node.select();
            }
            if (restore.expanded.delete(node.id)) {
                node.expand();
            }
        });
    };
    tree.on('model.loaded', restoreState);
    tree.on('children.loaded', (node) => restoreState(node.children));

    return {
        refresh() {
            restore.expanded = new Set(tree.expanded().map(node => node.id));
            restore.selected = tree.selected()[0]?.id ?? null;
            return tree.reload();
        }
    };
}
