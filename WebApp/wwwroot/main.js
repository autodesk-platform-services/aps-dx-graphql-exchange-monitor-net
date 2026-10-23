import { initViewer, loadModel, toUrn } from './viewer.js';
import { initTree } from './sidebar.js';
import { initSidebarLayout } from './layout.js';
import { initHeader } from './header.js';

initSidebarLayout();

const CALLBACK_KEY = 'exchange-monitor:callback-url';
const KIND_LABELS = { revit: 'Revit file', exchange: 'exchange' };
const picked = { revit: null, exchange: null };
let open = null; // the design open in the viewer
const $ = (id) => document.getElementById(id);

initDialog();
$('callback-url').value = localStorage.getItem(CALLBACK_KEY) || (location.protocol === 'https:' ? `${location.origin}/webhook` : '');
$('callback-url').addEventListener('input', updateRegister);
$('register').onclick = register;
$('use-open').onclick = () => {
    picked[open.kind] = open;
    showPick(open.kind);
    updateRegister();
};
updateRegister();

try {
    const user = await initHeader();
    if (!user) {
        $('tree').textContent = 'Log in to browse your hubs.';
        showPlaceholder('Log in to browse your hubs and open a design.');
        setStatus('Log in to pick the Revit file and the exchange.');
    } else {
        const viewer = await initViewer($('preview'));
        let loading = 0; // Ignore the result of a load if another design was picked in the meantime
        showPlaceholder('Select a Revit file or an exchange in the tree to open its latest version here.');
        const tree = initTree('#tree', async (design) => {
            hidePlaceholder();
            const current = ++loading;
            showOpen(null);
            let doc = null;
            try {
                const resp = await fetch(`/api/hubs/${design.hubId}/projects/${design.projectId}/contents/${design.itemId}/tip`);
                if (!resp.ok) {
                    throw new Error(await resp.text());
                }
                const { id: versionId } = await resp.json();
                doc = await loadModel(viewer, toUrn(versionId));
            } catch (err) {
                alert('Could not open the latest version. See console for more details.');
                console.error(err);
            }
            if (current === loading) {
                showOpen(doc ? design : null);
            }
        });
        const refresh = $('tree-refresh');
        refresh.onclick = async () => {
            refresh.disabled = true;
            try {
                await tree.refresh(); // e.g., to see a newly created exchange
            } finally {
                refresh.disabled = false;
            }
        };
        refresh.hidden = false;
    }
} catch (err) {
    alert('Could not initialize the application. See console for more details.');
    console.error(err);
}

// The design open in the viewer can be used as the Revit file or as the exchange, by what it is
function showOpen(design) {
    open = design;
    $('open-design').hidden = !design;
    $('open-hint').hidden = !!design;
    if (design) {
        $('open-name').textContent = `${design.name} (${KIND_LABELS[design.kind]})`;
        $('use-open').textContent = `Use as ${KIND_LABELS[design.kind]}`;
    }
}

function showPick(kind) {
    const value = $(`${kind}-value`);
    value.textContent = picked[kind].name;
    value.classList.remove('empty');
    value.closest('.pick').classList.add('set');
}

function updateRegister() {
    const url = $('callback-url').value.trim();
    $('register').disabled = !(picked.revit && picked.exchange && /^https:\/\/\S+$/i.test(url));
}

function setStatus(text, kind = '') {
    $('status').textContent = text;
    $('status').className = `status ${kind}`;
}

async function register() {
    const callbackUrl = $('callback-url').value.trim();
    $('register').disabled = true;
    setStatus('Registering the webhook...');
    try {
        const ids = ({ hubId, projectId, itemId }) => ({ hubId, projectId, itemId });
        const resp = await fetch('/api/monitor', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ revit: ids(picked.revit), exchange: ids(picked.exchange), callbackUrl })
        });
        if (!resp.ok) {
            setStatus(await resp.text(), 'error');
            return;
        }
        localStorage.setItem(CALLBACK_KEY, callbackUrl);
        setStatus('Webhook registered. See it under All webhooks.', 'ok');
    } catch (err) {
        console.error(err);
        setStatus('Could not register the webhook. See console for more details.', 'error');
    } finally {
        updateRegister();
    }
}

// Shown over the viewer until a design is picked, so that the empty viewer doesn't look like it's loading
function showPlaceholder(text) {
    const placeholder = $('viewer-placeholder');
    placeholder.querySelector('.placeholder-text').textContent = text;
    placeholder.hidden = false;
}

function hidePlaceholder() {
    $('viewer-placeholder').hidden = true;
}

// The dialog floats over the viewer: drag it by its title, collapse it with the button
function initDialog() {
    const dialog = $('monitor-dialog');
    const handle = $('dialog-handle');
    const toggle = $('dialog-toggle');
    toggle.onclick = () => {
        const collapsed = dialog.classList.toggle('collapsed');
        toggle.textContent = collapsed ? '+' : '−';
        toggle.setAttribute('aria-expanded', String(!collapsed));
    };
    handle.addEventListener('pointerdown', (ev) => {
        if (ev.button !== 0 || ev.target.closest('button')) {
            return;
        }
        ev.preventDefault();
        handle.setPointerCapture(ev.pointerId);
        const frame = dialog.offsetParent.getBoundingClientRect();
        const box = dialog.getBoundingClientRect();
        // Switch from the initial right-anchored position to left/top, relative to the frame
        const startLeft = box.left - frame.left, startTop = box.top - frame.top;
        const startX = ev.clientX, startY = ev.clientY;
        dialog.style.right = 'auto';
        dialog.style.left = `${startLeft}px`;
        dialog.style.top = `${startTop}px`;
        const onMove = (ev) => {
            // Keep the title inside the frame
            const left = Math.min(Math.max(startLeft + ev.clientX - startX, 0), frame.width - box.width);
            const top = Math.min(Math.max(startTop + ev.clientY - startY, 0), frame.height - handle.offsetHeight);
            dialog.style.left = `${left}px`;
            dialog.style.top = `${top}px`;
        };
        const onEnd = (ev) => {
            handle.releasePointerCapture(ev.pointerId);
            handle.removeEventListener('pointermove', onMove);
            handle.removeEventListener('pointerup', onEnd);
            handle.removeEventListener('pointercancel', onEnd);
        };
        handle.addEventListener('pointermove', onMove);
        handle.addEventListener('pointerup', onEnd);
        handle.addEventListener('pointercancel', onEnd);
    });
}
