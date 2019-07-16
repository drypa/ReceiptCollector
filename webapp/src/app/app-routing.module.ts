import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {ReceiptsComponent} from './receipts/receipts.component';
import {MarketsComponent} from './markets/markets.component';
import {NewReceiptComponent} from "./new-receipt/new-receipt.component";
import {ReceiptDetailsComponent} from "./receipt-details/receipt-details.component";
import {NewMarketComponent} from "./new-market/new-market.component";
import {MarketDetailsComponent} from "./market-details/market-details.component";

const routes: Routes = [
  {path: '', redirectTo: '/receipts', pathMatch: 'full'},
  {path: 'receipts/add', component: NewReceiptComponent},
  {path: 'receipts/:id', component: ReceiptDetailsComponent},
  {path: 'receipts', component: ReceiptsComponent},

  {path: 'markets/add', component: NewMarketComponent},
  {path: 'markets/:id', component: MarketDetailsComponent},
  {path: 'markets', component: MarketsComponent},
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule {
}
