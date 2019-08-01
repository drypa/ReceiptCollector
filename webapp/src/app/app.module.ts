import {BrowserModule} from '@angular/platform-browser';
import {NgModule} from '@angular/core';

import {AppRoutingModule} from './app-routing.module';
import {AppComponent} from './app.component';
import {ReceiptsComponent} from './receipts/receipts.component';
import {MarketsComponent} from './markets/markets.component';
import {HttpClientModule} from '@angular/common/http';
import {NewReceiptComponent} from './new-receipt/new-receipt.component';
import {ReceiptDetailsComponent} from './receipt-details/receipt-details.component';
import {NewMarketComponent} from './new-market/new-market.component';
import {MarketDetailsComponent} from './market-details/market-details.component';
import {BrowserAnimationsModule} from '@angular/platform-browser/animations';
import {MatButtonModule, MatFormFieldModule, MatInputModule, MatAutocompleteModule} from '@angular/material';
import {MatCardModule} from '@angular/material/card';
import {ReactiveFormsModule} from '@angular/forms';
import {LoginComponent} from './login/login.component';
import {AuthorizedComponent} from './authorized/authorized.component';

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
    AuthorizedComponent
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
    MatAutocompleteModule
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule {
}
