import { Component, OnInit } from '@angular/core';
import { FormControl, FormGroup } from '@angular/forms';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss']
})
export class LoginComponent implements OnInit {

  loginForm = new FormGroup({
    loginText: new FormControl(''),
    passwordText: new FormControl(''),
  });

  constructor(private authService: AuthService) {

  }

  ngOnInit() {
  }

  onSubmit() {
    if (this.loginForm.valid) {
      const formValue = this.loginForm.value;
      this.authService.login(formValue.loginText, formValue.passwordText);
    }
  }
}
