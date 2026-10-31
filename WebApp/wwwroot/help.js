// Help dialog of the header's "?" button: the client ID to provision in ACC (from the server's APS_CLIENT_ID) and links

export function initHelp() {
    // Same markup on every page: the button goes before the Login button, the dialog at the end of the page
    const button = Object.assign(document.createElement('button'), { id: 'help', title: 'Help', textContent: '?' });
    button.setAttribute('aria-haspopup', 'dialog');
    button.setAttribute('aria-controls', 'help-dialog');
    document.getElementById('login').before(button);
    const dialog = Object.assign(document.createElement('dialog'), { id: 'help-dialog', className: 'hud-panel' });
    dialog.setAttribute('aria-labelledby', 'help-title');
    dialog.innerHTML = `
        <button class="help-close" title="Close" aria-label="Close">&times;</button>
        <h2 id="help-title">Helpers</h2>
        <ul>
            <li>
                <a href="https://get-started.aps.autodesk.com/#provision-access-in-other-products" target="_blank" rel="noopener">Provision</a>
                the client ID
                <span class="help-client"><input id="help-client-id" type="text" readonly value="..." aria-label="Client ID"><button id="help-copy" title="Copy the client ID">Copy</button></span>
                in your hub.
            </li>
            <li>Find the complete source code <a href="https://github.com/autodesk-platform-services/aps-dx-graphql-exchange-monitor-net" target="_blank" rel="noopener">on GitHub</a>.</li>
        </ul>
        <form method="dialog">
            <button class="help-ok">OK</button>
        </form>
    `;
    document.body.append(dialog);
    const input = document.getElementById('help-client-id');
    const copy = document.getElementById('help-copy');
    let loaded = false;

    button.onclick = async () => {
        dialog.showModal();
        if (!loaded) {
            try {
                const resp = await fetch('/api/auth/client');
                if (!resp.ok) {
                    throw new Error(await resp.text());
                }
                const { clientId } = await resp.json();
                showClientId(clientId);
                loaded = true;
            } catch (err) {
                showClientId('Could not read the client ID');
                console.error(err);
            }
        }
    };
    dialog.querySelector('.help-close').onclick = () => dialog.close();
    // A click on the backdrop lands on the dialog itself, outside its box
    dialog.addEventListener('click', (ev) => {
        const box = dialog.getBoundingClientRect();
        const inside = ev.clientX >= box.left && ev.clientX <= box.right && ev.clientY >= box.top && ev.clientY <= box.bottom;
        if (ev.target === dialog && !inside) {
            dialog.close();
        }
    });
    copy.onclick = async () => {
        await navigator.clipboard.writeText(input.value);
        copy.textContent = 'Copied';
        setTimeout(() => copy.textContent = 'Copy', 1500);
    };

    function showClientId(text) {
        input.value = text;
        input.style.width = `${text.length + 1}ch`; // Monospace font: one ch per character
    }
}
