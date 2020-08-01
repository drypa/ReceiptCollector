import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';

@Injectable({
  providedIn: 'root'
})
export class AuthService {

  constructor(private router: Router, private httpClient: HttpClient) {
  }

  get isLoggedIn() {
    return this.loggedIn$.asObservable();
  }

  public static authDataKey = 'authData';
  private loggedIn$ = new BehaviorSubject<boolean>(false);

  login(login: string, password: string) {
    sessionStorage.setItem(AuthService.authDataKey, `${btoa(login + ':' + password)}`);
    this.httpClient.post('api/login', {}).subscribe(() => {
      this.loggedIn$.next(true);
      this.router.navigate(['/'])
    }, () => {
      sessionStorage.removeItem(AuthService.authDataKey);
      this.loggedIn$.next(false);
      this.router.navigate(['/'])
    });
  }

  logout() {
    this.loggedIn$.next(false);
  }
}
