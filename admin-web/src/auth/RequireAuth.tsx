import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from './AuthContext';

/**
 * Route guard for everything behind the login. Waits for the session restore to finish so a page
 * reload does not flash the login screen at an already-authenticated parent.
 */
export default function RequireAuth() {
  const { isReady, isAuthenticated } = useAuth();

  if (!isReady) {
    return (
      <div className="route-loading" role="status" aria-live="polite">
        <span className="spinner" aria-hidden="true" />
        <span>Loading...</span>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <Outlet />;
}
