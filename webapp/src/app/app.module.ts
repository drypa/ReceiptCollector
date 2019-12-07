import {BrowserModule} from '@angular/platform-browser';
import {NgModule} from '@angular/core';
import {MatChipsModule} from '@angular/material/chips';
import {AppRoutingModule} from './app-routing.module';
import {AppComponent} from './app.component';
import {ReceiptsComponent} from './receipts/receipts.component';
import {MarketsComponent} from './markets/markets.component';
import {HTTP_INTERCEPTORS, HttpClientModule} from '@angular/common/http';
import {NewReceiptComponent} from './new-receipt/new-receipt.component';
import {ReceiptDetailsComponent} from './receipt-details/receipt-details.component';
import {NewMarketComponent} from './new-market/new-market.component';
import {MarketDetailsComponent} from './market-details/market-details.component';
import {BrowserAnimationsModule} from '@angular/platform-browser/animations';
import {MatAutocompleteModule, MatButtonModule, MatFormFieldModule, MatInputModule} from '@angular/material';
import {MatCardModule} from '@angular/material/card';
import {ReactiveFormsModule} from '@angular/forms';
import {LoginComponent} from './login/login.component';
import {AuthorizedComponent} from './authorized/authorized.component';
import {BasicAuthInterceptor} from "./basic-auth-interceptor";
import {MatSnackBarModule} from "@angular/material/snack-bar";
import {ReceiptItemsComponent} from './receipt-items/receipt-items.component';
import {MatDialogModule} from "@angular/material/dialog";
import {RequestResultComponent} from './request-result/request-result.component';

@NgModule({
  declarations: [
    AppComponent,
    ReceiptsComponent,
    MarketsComponent,
    NewReceiptComponent,
    ReceiptDetailsComponent,
    NewMarketComponent,
    MarketDetailsComponent,
    LoginComponent,
    AuthorizedComponent,
    ReceiptItemsComponent,
    RequestResultComponent
  ],
  imports: [
    BrowserModule,
    AppRoutingModule,
    HttpClientModule,
    BrowserAnimationsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatCardModule,
    MatInputModule,
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatChipsModule,
    MatSnackBarModule,
    MatDialogModule
  ],
  providers: [
    {provide: HTTP_INTERCEPTORS, useClass: BasicAuthInterceptor, multi: true}
  ],
  bootstrap: [AppComponent],
  entryComponents: [RequestResultComponent]
})
export class AppModule {
}
