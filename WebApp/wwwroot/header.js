import { keepSessionAlive } from './session.js';

// Shows who is signed in and sets up the Login/Logout button of the header. Returns the user, or null if nobody is signed in.
export async function initHeader() {
    const login = document.getElementById('login');
    const resp = await fetch('/api/auth/profile');
    if (resp.ok) {
        const user = await resp.json();
        document.getElementById('user').textContent = user.name;
        login.innerText = 'Logout';
        login.onclick = () => {
            const iframe = document.createElement('iframe');
            iframe.style.visibility = 'hidden';
            iframe.src = 'https://accounts.autodesk.com/Authentication/LogOut';
            document.body.appendChild(iframe);
            iframe.onload = () => {
                window.location.replace('/api/auth/logout');
                document.body.removeChild(iframe);
            };
        };
        login.style.visibility = 'visible';
        keepSessionAlive();
        return user;
    }
    document.getElementById('user').textContent = 'nobody';
    login.innerText = 'Login';
    login.onclick = () => window.location.replace('/api/auth/login');
    login.style.visibility = 'visible';
    return null;
}

// Escapes text for use in innerHTML
export function esc(value) {
    return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
