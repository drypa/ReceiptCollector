import { NavLink } from 'react-router-dom';
import { useAdmin } from '../hooks/useAdmin';
import './Sidebar.css';

export function Sidebar() {
  const { isAdmin } = useAdmin();

  return (
    <aside className="sidebar">
      <nav className="sidebar-nav">
        <ul>
          <li>
            <NavLink to="/" end className={({ isActive }) => isActive ? 'sidebar-link active' : 'sidebar-link'}>
              Чеки
            </NavLink>
          </li>
          <li>
            <NavLink to="/commodities" className={({ isActive }) => isActive ? 'sidebar-link active' : 'sidebar-link'}>
              Товары
            </NavLink>
          </li>
          {isAdmin && (
            <li>
              <NavLink to="/merchants" className={({ isActive }) => isActive ? 'sidebar-link active' : 'sidebar-link'}>
                Магазины
              </NavLink>
            </li>
          )}
        </ul>
      </nav>
    </aside>
  );
}
