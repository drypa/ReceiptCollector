import {Injectable} from '@angular/core';
import {BehaviorSubject} from "rxjs";
import {Router} from "@angular/router";

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private loggedIn$ = new BehaviorSubject<boolean>(false);

  constructor(private router: Router) {
  }

  get isLoggedIn() {
    return this.loggedIn$.asObservable();
  }

  login(login: string, password: string) {
    //todo: need implement login
    this.loggedIn$.next(true);
    this.router.navigate(['/'])
  }

  logout() {
    this.loggedIn$.next(false);
  }
}
