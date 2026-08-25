import { useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

function navLinkClass({ isActive }: { isActive: boolean }): string {
  return isActive ? 'nav-link nav-link--active' : 'nav-link';
}

/** Chrome around every authenticated page: identity, navigation and sign out. */
export default function Layout() {
  const { parent, logout } = useAuth();
  const navigate = useNavigate();
  const [isSigningOut, setIsSigningOut] = useState(false);

  async function handleSignOut(): Promise<void> {
    setIsSigningOut(true);
    try {
      await logout();
    } finally {
      // The local session is dropped even if the server-side revocation call fails, so the parent
      // is never trapped in the app by a network error.
      setIsSigningOut(false);
      navigate('/login', { replace: true });
    }
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="app-header__brand">
          ParentalTrack<span>Parent console</span>
        </div>

        <nav className="app-header__nav" aria-label="Main">
          <NavLink to="/" end className={navLinkClass}>
            Dashboard
          </NavLink>
          <NavLink to="/devices" className={navLinkClass}>
            Devices
          </NavLink>
        </nav>

        <div className="app-header__spacer" />

        <div className="app-header__user">
          {parent === null ? null : (
            <span>
              Signed in as <strong>{parent.displayName}</strong>
            </span>
          )}
          <button
            type="button"
            className="btn btn--sm"
            disabled={isSigningOut}
            onClick={() => void handleSignOut()}
          >
            {isSigningOut ? 'Signing out...' : 'Sign out'}
          </button>
        </div>
      </header>

      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
