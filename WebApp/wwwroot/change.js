import { initHeader, esc } from './header.js';

const $ = (id) => document.getElementById(id);
let current = null;   // the webhook call that the shown result belongs to (the newest one)
let mode = null;      // details of the call: null (hidden) | 'table' | 'raw'
let checking = false;
let hookId = null;   // the registered webhook whose exchange is shown (?hook=)

try {
    const user = await initHeader();
    if (!user) {
        $('subject').textContent = 'Log in to query the exchange.';
    } else {
        await init();
    }
} catch (err) {
    console.error(err);
    $('subject').textContent = 'Could not initialize the page. See console for more details.';
}

async function init() {
    const requested = new URLSearchParams(location.search).get('hook');
    // The webhooks registered here; without ?hook=, the only one
    const items = await (await fetch('/api/monitor/registrations')).json();
    const registration = requested ? items.find(r => r.hookId === requested) : items.length === 1 ? items[0] : null;
    if (!registration) {
        $('subject').innerHTML = items.length === 0
            ? 'No webhooks registered yet. <a href="/">Register a Revit file and an exchange</a> first.'
            : 'Pick a webhook on the <a href="/watchers">Watchers</a> page.';
        return;
    }
    const config = registration;
    hookId = config.hookId;
    $('subject').innerHTML = `Comparing the latest version of the exchange <b>${esc(config.exchange.name)}</b> (Revit file <b>${esc(config.revit.name)}</b>) with the previous one. The result updates when a webhook call arrives.`;
    $('check').onclick = check;
    $('check').disabled = false;
    $('details-toggle').onclick = () => { mode = mode ? null : 'table'; renderDetails(); };
    current = await newestEvent();
    showTrigger();
    setInterval(watchForCallbacks, 3000);
    await check();
}

async function newestEvent() {
    try {
        const events = await (await fetch(`/api/events?hook=${encodeURIComponent(hookId)}`)).json();
        return events[0] ?? null;
    } catch {
        return current; // server briefly unavailable
    }
}

// A new webhook call: check the exchange right away
async function watchForCallbacks() {
    const newest = await newestEvent();
    if (newest && newest.id !== current?.id) {
        current = newest;
        mode = null;
        showTrigger();
        await check();
    }
}

function showTrigger() {
    $('details-toggle').hidden = !current;
    if (!current) {
        $('trigger').textContent = 'No webhook call received yet.';
        renderDetails();
        return;
    }
    // The hook note names the Revit file that changed (the payload itself is about the exchange)
    const note = bodyOf(current).hook?.hookAttribute?.note;
    $('trigger').textContent = `Webhook call #${current.id} received ${new Date(current.receivedAt).toLocaleTimeString()}${note ? `: ${note}` : ''}`;
    renderDetails();
}

async function check() {
    if (checking) {
        return;
    }
    checking = true;
    $('check').disabled = true;
    showVerdict('Querying the exchange...', '');
    try {
        const resp = await fetch(`/api/change?hook=${encodeURIComponent(hookId)}`);
        if (!resp.ok) {
            showVerdict(await resp.text(), 'error');
            return;
        }
        const { changes } = await resp.json();
        showChanges(changes);
    } catch (err) {
        console.error(err);
        showVerdict('Could not query the exchange. See console for more details.', 'error');
    } finally {
        checking = false;
        $('check').disabled = false;
    }
}

function showVerdict(text, kind) {
    $('verdict').textContent = text;
    $('verdict').className = `verdict ${kind}`;
    $('verdict').hidden = false;
    $('changes').hidden = true;
}

function showChanges(changes) {
    if (changes.length === 0) {
        showVerdict('Nothing changed: the latest version of the exchange has the same elements as the previous one.', 'nothing');
        return;
    }
    showVerdict(`${changes.length} element${changes.length === 1 ? '' : 's'} of the exchange changed.`, 'changed');
    // Elements without an "Element Name" property show their name (the Revit element ID or the type name)
    $('rows').innerHTML = changes.map(change => `
        <tr>
            <td>${change.elementName ? esc(change.elementName) : `<span class="unnamed" title="No Element Name property">${esc(change.name)}</span>`}</td>
            <td><span class="op ${esc(change.operation)}">${esc(change.operation)}</span></td>
        </tr>`).join('');
    $('changes').hidden = false;
}

// Details of the webhook call, as in the former watcher page: headers, payload and hook as tables, or the raw request

// A body that is not JSON is a plain string
function bodyOf(ev) {
    return typeof ev.body === 'object' && ev.body ? ev.body : {};
}

const el = (tag, text, attrs = {}) => {
    const e = document.createElement(tag);
    if (text !== undefined) e.textContent = text;
    return Object.assign(e, attrs);
};

// Raw format as received: request line, headers, then body
function rawText(ev) {
    const headers = Object.entries(ev.headers).map(([k, v]) => `  ${k}: ${v}`).join('\n');
    return `${ev.method} ${ev.url}\n{\n${headers}\n}\n${JSON.stringify(ev.body, null, 2)}`;
}

function kvTable(obj) {
    const t = el('table');
    for (const [k, v] of Object.entries(obj)) {
        const tr = el('tr');
        tr.append(el('th', k));
        const td = el('td');
        if (v !== null && typeof v === 'object') td.append(el('pre', JSON.stringify(v, null, 2)));
        else td.append(el('div', String(v), { className: 'val' })); // scrolls horizontally if too long
        tr.append(td);
        t.append(tr);
    }
    return t;
}

function renderDetails() {
    const box = $('details');
    box.replaceChildren();
    box.hidden = !mode || !current;
    $('details-toggle').textContent = mode ? 'Hide webhook call details' : 'Webhook call details';
    if (box.hidden) {
        return;
    }
    const tabs = el('div', undefined, { className: 'tabs' });
    for (const m of ['table', 'raw']) {
        const b = el('button', m === 'table' ? 'Table' : 'Raw', { disabled: m === mode });
        b.onclick = () => { mode = m; renderDetails(); };
        tabs.append(b, ' ');
    }
    box.append(tabs);
    if (mode === 'raw') {
        box.append(el('pre', rawText(current)));
        return;
    }
    const body = typeof current.body === 'object' && current.body ? current.body : { body: current.body };
    box.append(el('h3', 'Headers'), kvTable(current.headers));
    box.append(el('h3', 'Payload'), kvTable(body.payload || {}));
    box.append(el('h3', 'Hook'), kvTable(body.hook || {}));
    const rest = Object.fromEntries(Object.entries(body).filter(([k]) => k !== 'payload' && k !== 'hook'));
    if (Object.keys(rest).length) box.append(el('h3', 'Other'), kvTable(rest));
}
