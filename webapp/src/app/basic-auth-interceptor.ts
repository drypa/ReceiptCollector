import { Injectable } from '@angular/core';
import { HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService } from './auth.service';

@Injectable()
export class BasicAuthInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const authData = sessionStorage.getItem(AuthService.authDataKey);
    if (authData) {
      req = req.clone({ setHeaders: { Authorization: `Basic ${authData}` } })
    }
    return next.handle(req);
  }

}
