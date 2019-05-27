import {BrowserModule} from '@angular/platform-browser';
import {NgModule} from '@angular/core';

import {AppRoutingModule} from './app-routing.module';
import {AppComponent} from './app.component';
import {ReceiptsComponent} from './receipts/receipts.component';
import {MarketsComponent} from './markets/markets.component';
import { AddReceiptComponent } from './add-receipt/add-receipt.component';

@NgModule({
  declarations: [
    AppComponent,
    ReceiptsComponent,
    MarketsComponent,
    AddReceiptComponent
  ],
  imports: [
    BrowserModule,
    AppRoutingModule
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule {
}
