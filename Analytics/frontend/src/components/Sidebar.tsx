import { NavLink } from 'react-router-dom';
import './Sidebar.css';

export function Sidebar() {
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
        </ul>
      </nav>
    </aside>
  );
}
