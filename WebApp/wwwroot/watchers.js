import { initHeader } from './header.js';

const $ = (id) => document.getElementById(id);
const el = (tag, text, attrs = {}) => {
    const e = document.createElement(tag);
    if (text !== undefined) e.textContent = text;
    return Object.assign(e, attrs);
};

initHeader().catch(console.error);

let signature = '';

function setStatus(text, kind = 'error') {
    $('status').textContent = text;
    $('status').className = `status ${kind}`;
}

// Every webhook of the account, as listed by the Webhooks API, with how many calls this app received for each one
async function refresh() {
    $('reload').disabled = true;
    try {
        const resp = await fetch('/api/monitor');
        if (resp.status === 401) {
            setStatus('Log in to list the webhooks.');
            return;
        }
        if (!resp.ok) {
            setStatus(await resp.text());
            return;
        }
        setStatus('');
        const hooks = await resp.json();
        // Skip re-render when nothing changed
        const next = JSON.stringify(hooks);
        if (next !== signature) {
            signature = next;
            render(hooks);
        }
    } catch (e) {
        console.error(e);
        setStatus('Could not list the webhooks. See console for more details.');
    } finally {
        $('reload').disabled = false;
    }
}

function render(hooks) {
    const rows = $('rows');
    rows.replaceChildren();
    $('count').textContent = hooks.length;
    $('empty').hidden = hooks.length > 0;
    for (const hook of hooks) {
        const r = hook.registration;
        const tr = el('tr');
        for (const v of [
            `${hook.system}: ${hook.event}`, hook.callbackUrl, hook.note ?? '', r?.revit.name ?? '', r?.exchange.name ?? '',
            hook.status, hook.createdDate ? new Date(hook.createdDate).toLocaleString() : '',
            hook.calls, hook.lastCallAt ? new Date(hook.lastCallAt).toLocaleString() : '-',
        ]) tr.append(el('td', v));
        const last = el('td');
        if (r) {
            last.append(el('a', 'Go to changes', { href: `/change?hook=${encodeURIComponent(hook.hookId)}` }), ' ');
        }
        const remove = el('button', 'Delete');
        remove.onclick = () => deleteHook(hook, remove);
        last.append(remove);
        tr.append(last);
        rows.append(tr);
    }
}

async function deleteHook(hook, button) {
    if (!confirm(`Delete the webhook for ${hook.event} that calls ${hook.callbackUrl}?`)) {
        return;
    }
    button.disabled = true;
    try {
        const query = new URLSearchParams({ system: hook.system, event: hook.event });
        const resp = await fetch(`/api/monitor/${encodeURIComponent(hook.hookId)}?${query}`, { method: 'DELETE' });
        if (!resp.ok) {
            setStatus(resp.status === 401 ? 'Log in to delete a webhook.' : await resp.text());
            return;
        }
        signature = '';
        await refresh();
    } finally {
        button.disabled = false;
    }
}

$('reload').onclick = refresh;
refresh();
// The calls received change while the page is open; the list itself rarely does, and each refresh is a call to the Webhooks API
setInterval(refresh, 10000);
